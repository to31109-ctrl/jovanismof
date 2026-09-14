// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using System.Security.Cryptography;
using System.Text.Json;
namespace MegabonkTogether.Common.Persistence;

/// <summary>
/// Checksummed, crash-safe storage for co-op checkpoints. Writes go to a temporary file
/// and replace the previous one atomically, so an interrupted save can never destroy a
/// resumable checkpoint: the reader falls back to the retained ".previous" copy.
/// </summary>
public sealed class WorldSaveStore
{
    public const int MaximumBytes = 64 * 1024 * 1024;
    public const int Schema = 3;
    /// <summary>
    /// The oldest layout still readable. Schema 3 only adds the stat upgrades a character had,
    /// so a schema 2 world still loads: it simply restores without them, exactly as it did
    /// when it was written. Refusing it instead would make every existing world disappear from
    /// the player's list the moment they updated.
    /// </summary>
    public const int OldestReadableSchema = 2;
    public const int MaximumInLobby = 5;
    public const int MaximumRoster = 64;
    private readonly string directory;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, MaxDepth = 64 };

    public WorldSaveStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }

    public string Root => directory;

    public string PathFor(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidDataException("World identity is missing");
        return Path.Combine(directory, id.ToString("N") + ".bonkworld");
    }

    public void Write(WorldSave save)
    {
        Validate(save);
        var payload = JsonSerializer.SerializeToUtf8Bytes(save, Options);
        if (payload.Length > MaximumBytes / 2) throw new InvalidDataException("World save exceeds size limit");
        var envelope = new SaveEnvelope { Payload = payload, Digest = Convert.ToHexString(SHA256.HashData(payload)) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        lock (gate)
        {
            var path = PathFor(save.WorldId);
            if (File.Exists(path))
            {
                var previous = ReadFile(path);
                if (previous.HostId != save.HostId) throw new InvalidDataException("World belongs to another host");
                if (save.Revision <= previous.Revision) throw new InvalidDataException("Refusing to overwrite a newer checkpoint");
            }
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                // Atomic replacement keeps either the old checkpoint or the complete new one.
                if (File.Exists(path)) File.Replace(temp, path, path + ".previous", true);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public WorldSave Read(Guid id, bool previous = false)
    {
        lock (gate)
        {
            var save = ReadFile(PathFor(id) + (previous ? ".previous" : ""));
            if (save.WorldId != id) throw new InvalidDataException("World identity does not match its file");
            return save;
        }
    }

    /// <summary>Reads a checkpoint, silently falling back to the retained previous copy.</summary>
    public bool TryRead(Guid id, out WorldSave save, out string error)
    {
        error = "";
        try { save = Read(id); return true; }
        catch (Exception first)
        {
            try { save = Read(id, previous: true); error = "recovered previous checkpoint after: " + first.Message; return true; }
            catch (Exception second) { error = first.Message + "; previous copy: " + second.Message; save = null!; return false; }
        }
    }

    public IEnumerable<Guid> ListWorlds() => System.IO.Directory.EnumerateFiles(directory, "*.bonkworld")
        .Select(Path.GetFileNameWithoutExtension).Where(n => Guid.TryParseExact(n, "N", out _)).Select(n => Guid.ParseExact(n!, "N")).ToArray();

    /// <summary>Every readable checkpoint, newest first. Unreadable files are skipped, not thrown.</summary>
    public IReadOnlyList<WorldSave> ListReadable()
    {
        var found = new List<WorldSave>();
        foreach (var id in ListWorlds()) if (TryRead(id, out var save, out _)) found.Add(save);
        return found.OrderByDescending(s => s.SavedAt).ThenByDescending(s => s.Revision).ToArray();
    }

    /// <summary>The newest resumable checkpoint saved by this host, if any.</summary>
    public WorldSave? Latest(Guid hostId) => ListReadable()
        .FirstOrDefault(s => (hostId == Guid.Empty || s.HostId == hostId) && s.Resumable);

    public void Delete(Guid id)
    {
        lock (gate)
        {
            var path = PathFor(id);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".previous")) File.Delete(path + ".previous");
        }
    }

    /// <summary>Keeps the newest <paramref name="keep"/> worlds and deletes older ones.</summary>
    public int Prune(int keep)
    {
        if (keep < 1) throw new ArgumentOutOfRangeException(nameof(keep));
        var removed = 0;
        foreach (var stale in ListReadable().Skip(keep)) { Delete(stale.WorldId); removed++; }
        return removed;
    }

    private static WorldSave ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("World save exceeds size limit");
        var envelope = JsonSerializer.Deserialize<SaveEnvelope>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException("Empty save");
        if (envelope.Format != "BonkLinkWorld1" || envelope.Payload == null || envelope.Digest == null) throw new InvalidDataException("Unsupported save container");
        if (!string.Equals(envelope.Digest, Convert.ToHexString(SHA256.HashData(envelope.Payload)), StringComparison.Ordinal)) throw new InvalidDataException("World save checksum mismatch");
        var result = JsonSerializer.Deserialize<WorldSave>(envelope.Payload, Options) ?? throw new InvalidDataException("Empty world");
        Validate(result);
        return result;
    }

    public static void Validate(WorldSave save)
    {
        if (save.Schema > Schema || save.Schema < OldestReadableSchema) throw new InvalidDataException("Unsupported world save version");
        if (save.Scaling == null || !save.Scaling.IsValid()) throw new InvalidDataException("Invalid lobby scaling");
        if (save.WorldId == Guid.Empty || save.HostId == Guid.Empty || save.Revision < 1) throw new InvalidDataException("Invalid world identity/revision");
        // A world keeps everyone who has ever played it, so the roster outgrows a single lobby.
        if (save.Players == null || save.Players.Count is < 1 or > MaximumRoster || save.Players.Count(p => p.Connected) > MaximumInLobby
            || save.Enemies == null || save.Objects == null || save.BossOrbs == null
            || save.Pickups == null || save.Systems == null || save.MissingState == null || save.Progress == null) throw new InvalidDataException("Invalid world collections");
        if (save.Pickups.Count > 100000) throw new InvalidDataException("Pickup limit exceeded");
        foreach (var pickup in save.Pickups)
            if (pickup == null || pickup.Value < 0 || !IsFinite(pickup.Pose) || !float.IsFinite(pickup.ReadyDelay) || pickup.ReadyDelay < 0)
                throw new InvalidDataException("Invalid pickup state");
        if (save.Players.Any(p => p == null || p.PlayerId == Guid.Empty) || save.Players.Select(p => p.PlayerId).Distinct().Count() != save.Players.Count) throw new InvalidDataException("Invalid or duplicate player identity");
        if (!save.Players.Any(p => p.PlayerId == save.HostId && p.IsHost) || save.Players.Count(p => p.IsHost) != 1) throw new InvalidDataException("Invalid host record");
        // One saved run per player per character: the same person may appear more than once,
        // but never twice for the same character.
        if (save.Players.Where(p => !string.IsNullOrEmpty(p.Identity)).GroupBy(p => (p.Identity, p.Character)).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate player installation identity");
        if (save.Players.Where(p => p.Connected && !string.IsNullOrEmpty(p.Identity)).GroupBy(p => p.Identity).Any(g => g.Count() > 1)) throw new InvalidDataException("A player is connected as more than one character");
        if (save.Enemies.Count > 50000 || save.Objects.Count > 100000 || save.BossOrbs.Count > 5000 || save.Progress.ConsumedObjects.Count > 100000) throw new InvalidDataException("World entity limit exceeded");
        foreach (var consumed in save.Progress.ConsumedObjects)
            if (consumed == null || !IsFinite(consumed.Pose)) throw new InvalidDataException("Invalid consumed object");
        if (save.Enemies.Select(e => e.NetworkId).Distinct().Count() != save.Enemies.Count) throw new InvalidDataException("Duplicate enemy identity");
        if (!double.IsFinite(save.ElapsedSeconds) || save.ElapsedSeconds < 0) throw new InvalidDataException("Invalid world time");
        // A checkpoint is only offered for resume when the run it belongs to can actually be rebuilt.
        if (save.Resumable && string.IsNullOrEmpty(save.Stage)) throw new InvalidDataException("Resumable checkpoint has no stage");
        foreach (var enemy in save.Enemies)
        {
            if (!float.IsFinite(enemy.EchoDamage) || enemy.Debuffs == null || enemy.Debuffs.Count > 64 || enemy.Debuffs.Any(d => d == null || d.Ticks < 0 || d.Stacks < 0 || !float.IsFinite(d.Damage) || !float.IsFinite(d.ProcCoefficient))) throw new InvalidDataException("Invalid enemy effect");
            if (enemy.AttackCooldowns == null || enemy.AttackCooldowns.Count > 256 || enemy.AttackCooldowns.Any(c => string.IsNullOrEmpty(c.Key) || !float.IsFinite(c.Value) || c.Value < 0)) throw new InvalidDataException("Invalid attack cooldown");
            if (!float.IsFinite(enemy.Hp) || !float.IsFinite(enemy.MaxHp) || enemy.Hp < 0 || enemy.MaxHp < 0) throw new InvalidDataException("Invalid enemy health");
            if (!IsFinite(enemy.Pose) || !float.IsFinite(enemy.SizeMultiplier) || !float.IsFinite(enemy.SpeedMultiplier)) throw new InvalidDataException("Invalid enemy placement");
        }
        foreach (var orb in save.BossOrbs)
            if (!float.IsFinite(orb.Hp) || orb.Hp < 0 || !IsFinite(orb.Pose)) throw new InvalidDataException("Invalid boss orb state");
        foreach (var player in save.Players)
        {
            if (player.MaxHp < 0 || player.Hp < 0 || player.Hp > player.MaxHp + 1) throw new InvalidDataException("Invalid player health");
            if (!float.IsFinite(player.Shield) || !float.IsFinite(player.MaxShield) || !float.IsFinite(player.Overheal) || !float.IsFinite(player.LeftOverXp)) throw new InvalidDataException("Invalid player health");
            if (player.Shield < 0 || player.Overheal < 0 || player.Level < 0 || player.Xp < 0) throw new InvalidDataException("Invalid player progression");
            if (!IsFinite(player.Pose)) throw new InvalidDataException("Invalid player placement");
            if (player.Items == null || player.Upgrades == null || player.Components == null || player.Stats == null) throw new InvalidDataException("Invalid player collections");
            // A character restored from an unusable stat would come back silently weaker.
            if (player.Stats.Any(m => m == null || !float.IsFinite(m.Value))) throw new InvalidDataException("Invalid stat upgrade");
            if (player.Items.Values.Any(v => v < 0)) throw new InvalidDataException("Invalid item amount");
            foreach (var upgrade in player.Upgrades)
            {
                if (upgrade.Kind is not (SavedUpgrade.WeaponKind or SavedUpgrade.TomeKind)) throw new InvalidDataException("Unknown upgrade kind");
                if (upgrade.Level < 0 || upgrade.Levels == null) throw new InvalidDataException("Invalid upgrade level");
                if (upgrade.Levels.Any(l => l?.Modifiers == null || l.Modifiers.Any(m => !float.IsFinite(m.Value)))) throw new InvalidDataException("Invalid stat modifier");
            }
        }
    }

    private static bool IsFinite(SavedPose pose) => pose != null
        && float.IsFinite(pose.X) && float.IsFinite(pose.Y) && float.IsFinite(pose.Z)
        && float.IsFinite(pose.Pitch) && float.IsFinite(pose.Yaw) && float.IsFinite(pose.Roll);

    private sealed class SaveEnvelope
    {
        public string Format { get; set; } = "BonkLinkWorld1";
        public string Digest { get; set; } = "";
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }
}
