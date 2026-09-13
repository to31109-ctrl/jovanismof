// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
namespace MegabonkTogether.Common.Persistence;

/// <summary>
/// A complete co-op checkpoint. This is deliberately separate from the game's own
/// progression save: nothing here is ever written into the player's normal profile.
/// </summary>
public sealed class WorldSave
{
    public int Schema { get; set; } = 2;
    public Guid WorldId { get; set; }
    public Guid HostId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public string GameVersion { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public string Name { get; set; } = "Co-op world";
    public int Map { get; set; }
    public string Stage { get; set; } = "";
    public int StageIndex { get; set; }
    public int Tier { get; set; }
    public string Challenge { get; set; } = "";
    public int Music { get; set; }
    public int Seed { get; set; }
    public double ElapsedSeconds { get; set; }
    public bool SharedRewards { get; set; }
    public Models.LobbyScaling Scaling { get; set; } = new();
    /// <summary>False when a critical adapter failed; such a checkpoint is never offered for resume.</summary>
    public bool Resumable { get; set; }
    public WorldProgress Progress { get; set; } = new();
    public List<SavedPlayer> Players { get; set; } = new();
    public List<SavedEnemy> Enemies { get; set; } = new();
    public List<SavedBossOrb> BossOrbs { get; set; } = new();
    public List<SavedPickup> Pickups { get; set; } = new();
    public List<SavedObject> Objects { get; set; } = new();
    public Dictionary<string, SavedComponent> Systems { get; set; } = new();
    // A checkpoint must not be advertised as resumable if an adapter could not capture its state.
    public List<string> MissingState { get; set; } = new();

    /// <summary>
    /// This installation's saved run for one character, or null when they have never played that
    /// character here. Matching is on installation identity, never a display name: a name is not
    /// proof of who someone is, and handing a newcomer another player's character would be worse
    /// than starting them fresh. Picking a character you have not played on this world is a fresh
    /// start for that character, and leaves the ones you have played untouched.
    /// </summary>
    public SavedPlayer? FindPlayer(string identity, int character) =>
        string.IsNullOrEmpty(identity)
            ? null
            : Players.FirstOrDefault(p => p.Identity == identity && p.Character == character);

    /// <summary>The character this installation most recently played on this world, if any.</summary>
    public SavedPlayer? FindMostRecent(string identity) =>
        string.IsNullOrEmpty(identity)
            ? null
            : Players.FirstOrDefault(p => p.Identity == identity && p.Connected)
              ?? Players.FirstOrDefault(p => p.Identity == identity);

    /// <summary>How many of the remembered players were in the run when it was checkpointed.</summary>
    public int ConnectedCount => Players.Count(p => p.Connected);
}

/// <summary>Run-wide flags the stage itself does not rebuild on load.</summary>
public sealed class WorldProgress
{
    public bool IsCrypt { get; set; }
    public int CryptIndex { get; set; }
    public int BossCurses { get; set; }
    public bool EnteredBossRoom { get; set; }
    public bool FinalBossDead { get; set; }
    public bool DungeonTimerStarted { get; set; }
    public float DungeonTimeToComplete { get; set; }
    public bool DungeonOvertime { get; set; }
    public bool FinalSwarm { get; set; }
    /// <summary>
    /// Chests, shrines and other interactables the players already used on this stage. The stage
    /// is rebuilt from its seed, so these are matched by what they are and where they stood.
    /// </summary>
    public List<SavedObject> ConsumedObjects { get; set; } = new();
}

public sealed class SavedPlayer
{
    public Guid PlayerId { get; set; }
    /// <summary>Stable per-installation identity, so a returning player finds their own slot.</summary>
    public string Identity { get; set; } = "";
    public uint ConnectionId { get; set; }
    public string Name { get; set; } = "Player";
    public int Character { get; set; }
    public string Skin { get; set; } = "";
    public bool IsHost { get; set; }
    public bool Connected { get; set; }
    public bool Dead { get; set; }
    public SavedPose Pose { get; set; } = new();
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public float Overheal { get; set; }
    public float Shield { get; set; }
    public float MaxShield { get; set; }
    public int Gold { get; set; }
    public int Xp { get; set; }
    public int Level { get; set; }
    public float LeftOverXp { get; set; }
    public int Banishes { get; set; }
    public int Refreshes { get; set; }
    public int Skips { get; set; }
    /// <summary>Weapons and tomes, replayed in order so the game recomputes their stats.</summary>
    public List<SavedUpgrade> Upgrades { get; set; } = new();
    public Dictionary<int, int> Items { get; set; } = new();
    public Dictionary<string, SavedComponent> Components { get; set; } = new();

    public IEnumerable<SavedUpgrade> Weapons => Upgrades.Where(u => u.Kind == SavedUpgrade.WeaponKind);
    public IEnumerable<SavedUpgrade> Tomes => Upgrades.Where(u => u.Kind == SavedUpgrade.TomeKind);
}

public sealed class SavedUpgrade
{
    public const string WeaponKind = "weapon";
    public const string TomeKind = "tome";

    public string Kind { get; set; } = "";
    public int Type { get; set; }
    public int Level { get; set; }
    public int Rarity { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>One modifier set per acquired level, replayed in the order they were taken.</summary>
    public List<SavedModifierSet> Levels { get; set; } = new();
}

public sealed class SavedModifierSet
{
    public List<SavedModifier> Modifiers { get; set; } = new();
}

public sealed class SavedModifier
{
    public int Stat { get; set; }
    public int Operation { get; set; }
    public float Value { get; set; }
}

public sealed class SavedEnemy
{
    public uint NetworkId { get; set; }
    public int Species { get; set; }
    public int Flags { get; set; }
    public bool IsBoss { get; set; }
    public bool IsFinalBoss { get; set; }
    public bool IsElite { get; set; }
    public int Wave { get; set; }
    public uint TargetConnectionId { get; set; }
    public Guid TargetPlayerId { get; set; }
    public SavedPose Pose { get; set; } = new();
    public float Hp { get; set; }
    public float MaxHp { get; set; }
    public int Armor { get; set; }
    public int ArmorMax { get; set; }
    public float SizeMultiplier { get; set; } = 1f;
    public float SpeedMultiplier { get; set; } = 1f;
    public string ReviverFor { get; set; } = "";
    public Dictionary<string, float> AttackCooldowns { get; set; } = new();
    public List<SavedDebuff> Debuffs { get; set; } = new();
    public float EchoDamage { get; set; }
    public Dictionary<string, SavedComponent> Components { get; set; } = new();
}

public sealed class SavedDebuff
{
    public int Kind { get; set; }
    public int Ticks { get; set; }
    public int Stacks { get; set; }
    public float Damage { get; set; }
    public string Source { get; set; } = "";
    public bool Crit { get; set; }
    public int Flags { get; set; }
    public float ProcCoefficient { get; set; }
}

public sealed class SavedPickup
{
    public int Kind { get; set; }
    public int Value { get; set; }
    public float ReadyDelay { get; set; }
    public SavedPose Pose { get; set; } = new();
}

public sealed class SavedBossOrb
{
    public uint NetworkId { get; set; }
    public int Kind { get; set; }
    public float Hp { get; set; }
    public SavedPose Pose { get; set; } = new();
}

public sealed class SavedObject
{
    public uint NetworkId { get; set; }
    public string Prefab { get; set; } = "";
    public SavedPose Pose { get; set; } = new();
    public bool Active { get; set; }
    public Dictionary<string, SavedComponent> Components { get; set; } = new();
}

public sealed class SavedComponent
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
}

public sealed class SavedPose
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float Roll { get; set; }
}
