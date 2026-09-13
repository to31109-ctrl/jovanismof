// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using BepInEx.Logging;
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Common.Persistence;
using MegabonkTogether.Configuration;
using Assets.Scripts.Managers;
using MegabonkTogether.Helpers;
using MegabonkTogether.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MegabonkTogether.Services
{
    /// <summary>
    /// Co-op world checkpoints: the host writes the whole run — players, inventories, health,
    /// enemies including boss health, and run progress — to its own save directory, and can
    /// restore it later or hand a returning player their character back.
    /// </summary>
    public interface IWorldSaveService
    {
        /// <summary>Called once per frame on the host while a run is live.</summary>
        void Tick(float deltaTime);

        /// <summary>Writes a checkpoint immediately. Returns false when nothing was written.</summary>
        bool SaveNow(string reason);

        /// <summary>Host: pick up the newest checkpoint for the stage that is about to load.</summary>
        void PrepareResume(int map, string stage);

        /// <summary>Host: apply any prepared checkpoint once the stage and peers are live.</summary>
        void OnSessionStarted();

        /// <summary>Host: a client reported itself in-game; return its checkpointed slot if we hold one.</summary>
        void OnClientReady(uint connectionId, string identity);

        /// <summary>Any peer: apply the state the host sent us.</summary>
        void OnReceivedRestore(WorldRestore restore);

        /// <summary>Host: a world object was used up, so a resumed stage should not hand it back.</summary>
        void RecordObjectConsumed(string prefab, float x, float y, float z);

        /// <summary>Host: the stage is over, so what was used on it no longer applies to the next one.</summary>
        void OnStageFinished();

        /// <summary>
        /// Host: true when this freshly generated object was already used at the checkpoint being
        /// resumed. Answered before any peer is told the object exists, so nothing can desynchronise.
        /// </summary>
        bool ShouldSuppressSpawnedObject(string prefab, float x, float y, float z);

        /// <summary>
        /// Writes a final checkpoint and stops the session, whether the players left, were
        /// beaten or finished the run. The checkpoint stays on disk so the world can be resumed.
        /// </summary>
        void OnSessionEnded();

        /// <summary>The checkpoint that would be resumed for this stage, if any.</summary>
        WorldSave GetResumableFor(int map, string stage);

        /// <summary>Every saved world, newest first, for the host to choose between.</summary>
        IReadOnlyList<WorldSave> ListWorlds();

        /// <summary>
        /// The world the host chose to continue. Guid.Empty means a new world, where every player
        /// starts fresh no matter what any saved world remembers about them.
        /// </summary>
        Guid SelectedWorldId { get; set; }

        /// <summary>The character this installation last played on the chosen world, if any.</summary>
        int? RememberedCharacterFor(Guid worldId);

        string SaveDirectory { get; }
    }

    internal sealed class WorldSaveService : IWorldSaveService
    {
        private readonly ManualLogSource logger;
        private readonly IPlayerManagerService players;
        private readonly IEnemyManagerService enemies;
        private readonly IFinalBossOrbManagerService orbs;
        private readonly ISpawnedObjectManagerService objects;
        private readonly WorldSaveStore store;
        private readonly object gate = new();

        private static readonly JsonSerializerOptions Json = new() { MaxDepth = 64 };

        private Guid worldId = Guid.Empty;
        private Guid hostId = Guid.Empty;
        private long revision;
        private float sinceLastSave;
        private bool sessionLive;
        private WorldSave pendingResume;
        private WorldSave lastCheckpoint;
        private WorldProgress carriedProgress = new();
        // Restores can arrive before this peer finishes loading, so they wait for the session.
        private readonly List<WorldRestore> deferredRestores = new();
        // Objects the resumed checkpoint says were already used, consumed one match at a time
        // as the stage regenerates.
        private readonly List<SavedObject> resumeConsumed = new();
        // Connections already considered for a rejoin restore, so a stage change never
        // re-applies an earlier checkpoint over a player's live progress.
        private readonly HashSet<uint> restoredThisSession = new();
        private const int MaximumConsumedObjects = 20000;
        private const float ObjectMatchRadius = 1.25f;
        private const int MaximumFramesToWait = 600;

        public WorldSaveService(
            ManualLogSource logger,
            IPlayerManagerService players,
            IEnemyManagerService enemies,
            IFinalBossOrbManagerService orbs,
            ISpawnedObjectManagerService objects)
        {
            this.logger = logger;
            this.players = players;
            this.enemies = enemies;
            this.orbs = orbs;
            this.objects = objects;

            store = new WorldSaveStore(Path.Combine(BepInEx.Paths.BepInExRootPath, "BonkLinkWorlds"));
            hostId = ResolveIdentity();

            EventManager.SubscribeWorldRestoreEvents(OnReceivedRestore);
            logger.LogInfo($"Co-op world checkpoints {(ModConfig.CoopWorldSaves.Value ? "enabled" : "disabled")}; directory: {store.Root}");
        }

        public string SaveDirectory => store.Root;

        private ISynchronizationService Synchronization => Plugin.Services.GetService<ISynchronizationService>();

        private static Guid ResolveIdentity() =>
            Guid.TryParse(ModConfig.PlayerIdentity.Value, out var parsed) && parsed != Guid.Empty ? parsed : Guid.NewGuid();

        private bool Enabled => ModConfig.CoopWorldSaves.Value;

        private bool IsHost
        {
            get
            {
                try { return Synchronization?.IsServerMode() ?? false; }
                catch { return false; }
            }
        }

        public void Tick(float deltaTime)
        {
            if (!Enabled || !IsHost || !sessionLive) return;
            if (!float.IsFinite(deltaTime) || deltaTime <= 0) return;

            sinceLastSave += deltaTime;
            if (sinceLastSave < Math.Max(5f, ModConfig.CoopAutosaveSeconds.Value)) return;

            sinceLastSave = 0f;
            SaveNow("autosave");
        }

        public bool SaveNow(string reason)
        {
            if (!Enabled || !IsHost) return false;
            if (worldId == Guid.Empty && !sessionLive) return false;

            lock (gate)
            {
                try
                {
                    if (worldId == Guid.Empty) worldId = Guid.NewGuid();

                    var save = WorldCapture.Capture(worldId, hostId, revision + 1, players, enemies, orbs, objects, carriedProgress, lastCheckpoint);
                    save.Name = string.IsNullOrEmpty(save.Name) ? "Co-op world" : save.Name;

                    if (save.Players.Count == 0)
                    {
                        logger.LogWarning($"Co-op checkpoint ({reason}) skipped: no players captured");
                        return false;
                    }

                    store.Write(save);
                    revision = save.Revision;
                    lastCheckpoint = save;
                    carriedProgress = save.Progress;
                    sinceLastSave = 0f;

                    var gaps = save.MissingState.Count == 0 ? "" : $"; incomplete: {string.Join(", ", save.MissingState)}";
                    logger.LogInfo($"Co-op checkpoint {save.Revision} ({reason}): {save.Players.Count} players, {save.Enemies.Count} enemies, {save.ElapsedSeconds:F0}s{gaps}");

                    try { store.Prune(Math.Max(1, ModConfig.CoopWorldsKept.Value)); }
                    catch (Exception ex) { logger.LogWarning($"Co-op checkpoint pruning failed: {ex.Message}"); }

                    return true;
                }
                catch (Exception ex)
                {
                    logger.LogError($"Co-op checkpoint ({reason}) failed: {ex}");
                    return false;
                }
            }
        }

        public IReadOnlyList<WorldSave> ListWorlds()
        {
            try { return store.ListReadable().Where(s => s.Resumable).ToArray(); }
            catch (Exception ex)
            {
                logger.LogWarning($"Listing co-op worlds failed: {ex.Message}");
                return Array.Empty<WorldSave>();
            }
        }

        public Guid SelectedWorldId { get; set; } = Guid.Empty;

        public int? RememberedCharacterFor(Guid worldId)
        {
            if (worldId == Guid.Empty) return null;

            try
            {
                var world = ListWorlds().FirstOrDefault(w => w.WorldId == worldId);
                var mine = world?.FindMostRecent(ModConfig.PlayerIdentity.Value ?? "");
                return mine?.Character;
            }
            catch { return null; }
        }

        public WorldSave GetResumableFor(int map, string stage)
        {
            if (!Enabled) return null;

            try
            {
                return store.ListReadable().FirstOrDefault(s =>
                    s.Resumable && s.HostId == hostId && s.Map == map &&
                    string.Equals(s.Stage, stage, StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Co-op checkpoint lookup failed: {ex.Message}");
                return null;
            }
        }

        public void PrepareResume(int map, string stage)
        {
            pendingResume = null;

            // Loading the next stage of a run in progress must never pull in an older world.
            if (sessionLive) return;
            if (!Enabled) return;

            WorldSave candidate;

            if (SelectedWorldId != Guid.Empty)
            {
                // The host asked for this specific world, so nothing else may be substituted.
                candidate = ListWorlds().FirstOrDefault(w => w.WorldId == SelectedWorldId);

                if (candidate == null)
                {
                    logger.LogWarning("The chosen co-op world is no longer readable; starting a new one");
                    return;
                }

                if (candidate.Map != map || !string.Equals(candidate.Stage, stage, StringComparison.Ordinal))
                {
                    logger.LogWarning($"{candidate.Name} is saved on a different stage than the one starting; starting a new world instead");
                    return;
                }
            }
            else
            {
                // The picker defaults to New World, and New World means nothing is restored.
                logger.LogInfo("Starting a new co-op world; nothing from any saved world is restored.");
                lastCheckpoint = null;
                return;
            }

            if (candidate == null) return;

            pendingResume = candidate;
            resumeConsumed.Clear();
            resumeConsumed.AddRange(candidate.Progress.ConsumedObjects);

            // The stage is procedural: rebuilding it from the checkpoint's seed is what makes the
            // saved positions, chests and interactables line up with the world that comes back.
            if (candidate.Seed != 0)
            {
                players.SetSeed(candidate.Seed);
                UnityEngine.Random.InitState(candidate.Seed);
            }
            if (!Plugin.Instance.Mode.ScalingChosenByHost) Plugin.Instance.Mode.Scaling = candidate.Scaling;
            logger.LogInfo($"Co-op checkpoint {candidate.Revision} ({candidate.Name}) will be restored once the stage is live");
        }

        public void OnSessionStarted()
        {
            var wasLive = sessionLive;
            sessionLive = true;
            sinceLastSave = 0f;

            FlushDeferredRestores();

            if (!Enabled || !IsHost) return;

            var resume = pendingResume;
            pendingResume = null;

            if (resume == null)
            {
                if (!wasLive)
                {
                    // A fresh run gets its own world so an older checkpoint is never overwritten,
                    // and it must not remember anyone or anything from the previous one. Leaving
                    // the last checkpoint in place carried old players, their levels and their
                    // gold straight into a brand new world.
                    worldId = Guid.NewGuid();
                    revision = 0;
                    carriedProgress = new WorldProgress();
                    lastCheckpoint = null;
                    restoredThisSession.Clear();
                }

                // The remote players' inventories are built a frame or two after the stage
                // reports itself started, so the opening checkpoint waits for them too.
                CoroutineRunner.Instance.Run(SaveWhenStageIsReady(wasLive ? "stage started" : "run started"));
                return;
            }

            worldId = resume.WorldId;
            revision = resume.Revision;
            carriedProgress = resume.Progress;

            // The stage reports itself started before its enemy pools and the remote players'
            // inventories exist, so the restore waits for them rather than silently doing nothing.
            CoroutineRunner.Instance.Run(RestoreWhenStageIsReady(resume));
        }

        private IEnumerator SaveWhenStageIsReady(string reason)
        {
            for (var frame = 0; frame < MaximumFramesToWait && !IsStageReady(needsEnemyManager: false); frame++)
            {
                yield return null;
            }

            SaveNow(reason);
        }

        private IEnumerator RestoreWhenStageIsReady(WorldSave resume)
        {
            var needsEnemies = resume.Enemies.Count > 0;

            for (var frame = 0; frame < MaximumFramesToWait && !IsStageReady(needsEnemies); frame++)
            {
                yield return null;
            }

            if (!IsStageReady(needsEnemies))
            {
                logger.LogWarning($"Co-op world {resume.Name}: the stage never became ready; restoring what is available");
            }

            try
            {
                var slots = MatchSlotsToCurrentPlayers(resume);
                WorldApply.ApplyWorld(resume, enemies);
                var applied = WorldApply.ApplyPlayers(slots, players);
                Broadcast(slots, resume, rejoin: false);

                logger.LogInfo($"Co-op world {resume.Name} restored from checkpoint {resume.Revision}: {applied}/{slots.Count} players, {resume.Enemies.Count} enemies");
            }
            catch (Exception ex)
            {
                logger.LogError($"Co-op world restore failed, continuing with a fresh run: {ex}");
                worldId = Guid.NewGuid();
                revision = 0;
                carriedProgress = new WorldProgress();
            }
        }

        /// <summary>
        /// True once the pieces a restore writes into exist: the local player, the enemy manager
        /// that respawns the checkpointed wave, and an inventory for every remote player.
        /// </summary>
        private bool IsStageReady(bool needsEnemyManager)
        {
            try
            {
                if (GameManager.Instance?.player?.inventory == null) return false;
                if (needsEnemyManager && EnemyManager.Instance == null) return false;

                foreach (var player in players.GetAllPlayers())
                {
                    if (player == null || players.IsLocalConnectionId(player.ConnectionId)) continue;
                    if (players.GetNetPlayerByNetplayId(player.ConnectionId)?.Inventory == null) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void RecordObjectConsumed(string prefab, float x, float y, float z)
        {
            if (!Enabled || !IsHost || !sessionLive) return;
            if (string.IsNullOrEmpty(prefab)) return;
            if (carriedProgress.ConsumedObjects.Count >= MaximumConsumedObjects) return;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return;

            carriedProgress.ConsumedObjects.Add(new SavedObject
            {
                Prefab = prefab,
                Pose = new SavedPose { X = x, Y = y, Z = z },
            });
        }

        public void OnStageFinished()
        {
            carriedProgress.ConsumedObjects = new List<SavedObject>();
            resumeConsumed.Clear();
        }

        public bool ShouldSuppressSpawnedObject(string prefab, float x, float y, float z)
        {
            if (resumeConsumed.Count == 0 || string.IsNullOrEmpty(prefab)) return false;

            for (var index = 0; index < resumeConsumed.Count; index++)
            {
                var candidate = resumeConsumed[index];
                if (!string.Equals(candidate.Prefab, prefab, StringComparison.Ordinal)) continue;

                var dx = candidate.Pose.X - x;
                var dy = candidate.Pose.Y - y;
                var dz = candidate.Pose.Z - z;
                if (dx * dx + dy * dy + dz * dz > ObjectMatchRadius * ObjectMatchRadius) continue;

                // Each recorded pickup accounts for exactly one rebuilt object.
                resumeConsumed.RemoveAt(index);
                return true;
            }

            return false;
        }

        public void OnClientReady(uint connectionId, string identity)
        {
            if (!Enabled || !IsHost || !sessionLive) return;

            // Clients report themselves ready at the start of every stage, not only when they
            // rejoin. Restoring on each of those rewound players to an earlier checkpoint in the
            // middle of a run, which is what made gold and levels jump around.
            if (!restoredThisSession.Add(connectionId)) return;

            var source = lastCheckpoint;
            var character = (int)(players.GetPlayer(connectionId)?.Character ?? 0);
            var slot = source?.FindPlayer(identity, character);
            if (slot == null)
            {
                logger.LogInfo($"Player {connectionId} has not played this world before; they start fresh");
                return;
            }

            // Someone who was still in the run at the last checkpoint has not been away, so
            // there is nothing to give back and overwriting their live state would be wrong.
            if (slot.Connected)
            {
                logger.LogInfo($"{slot.Name} was already in this run; leaving their state alone");
                return;
            }

            // The returning player takes over their old slot under their new connection.
            slot.ConnectionId = connectionId;
            slot.Connected = true;

            try
            {
                WorldApply.ApplyPlayers(new[] { slot }, players);
                Broadcast(new[] { slot }, source, rejoin: true);
                logger.LogInfo($"Returning player {slot.Name} restored from checkpoint {source.Revision}");
            }
            catch (Exception ex)
            {
                logger.LogError($"Restoring returning player {slot.Name} failed: {ex}");
            }
        }

        public void OnReceivedRestore(WorldRestore restore)
        {
            if (restore == null || string.IsNullOrEmpty(restore.PlayersJson)) return;

            if (!sessionLive && !IsHost)
            {
                // The host can be ahead of us; hold the state until our own stage is running.
                deferredRestores.Add(restore);
                return;
            }

            try
            {
                var slots = JsonSerializer.Deserialize<List<SavedPlayer>>(restore.PlayersJson, Json);
                if (slots == null || slots.Count == 0) return;

                var applied = WorldApply.ApplyPlayers(slots, players);
                logger.LogInfo($"Applied host checkpoint {restore.Revision} ({(restore.Rejoin ? "rejoin" : "world resume")}): {applied}/{slots.Count} players");
            }
            catch (Exception ex)
            {
                logger.LogError($"Applying host checkpoint failed: {ex}");
            }
        }

        public void OnSessionEnded()
        {
            if (sessionLive && IsHost) SaveNow("session ended");

            sessionLive = false;
            pendingResume = null;
            resumeConsumed.Clear();
            lastCheckpoint = null;
            restoredThisSession.Clear();
            worldId = Guid.Empty;
            revision = 0;
            carriedProgress = new WorldProgress();
            deferredRestores.Clear();
            sinceLastSave = 0f;
        }

        private void FlushDeferredRestores()
        {
            if (deferredRestores.Count == 0) return;

            var held = deferredRestores.ToArray();
            deferredRestores.Clear();
            foreach (var restore in held) OnReceivedRestore(restore);
        }

        /// <summary>
        /// Re-keys checkpointed players onto the connections that are actually in the lobby now,
        /// matching on installation identity and falling back to name for older checkpoints.
        /// </summary>
        private List<SavedPlayer> MatchSlotsToCurrentPlayers(WorldSave save)
        {
            var matched = new List<SavedPlayer>();
            var taken = new HashSet<Guid>();

            foreach (var player in players.GetAllPlayers().ToArray())
            {
                if (player == null) continue;

                // Identity only. A display name is not proof of who someone is, and the default
                // name is shared by every fresh installation: matching on it would hand a newcomer
                // somebody else's character.
                var slot = string.IsNullOrEmpty(player.Identity)
                    ? null
                    : save.Players.FirstOrDefault(p => !taken.Contains(p.PlayerId)
                        && p.Identity == player.Identity
                        && p.Character == (int)player.Character);

                if (slot == null)
                {
                    var playedBefore = !string.IsNullOrEmpty(player.Identity) && save.Players.Any(p => p.Identity == player.Identity);
                    logger.LogInfo(playedBefore
                        ? $"{player.Name} is playing a character they have not used on this world; they start fresh"
                        : $"{player.Name} has not played this world before; they start fresh");
                    continue;
                }

                taken.Add(slot.PlayerId);
                slot.ConnectionId = player.ConnectionId;
                matched.Add(slot);
            }

            return matched;
        }

        private void Broadcast(IReadOnlyCollection<SavedPlayer> slots, WorldSave source, bool rejoin)
        {
            if (slots.Count == 0) return;

            try
            {
                var udp = Plugin.Services.GetService<IUdpClientService>();
                if (udp == null) return;

                IGameNetworkMessage message = new WorldRestore
                {
                    WorldId = source.WorldId.ToString("N"),
                    Revision = source.Revision,
                    ElapsedSeconds = source.ElapsedSeconds,
                    Rejoin = rejoin,
                    PlayersJson = JsonSerializer.Serialize(slots.ToList(), Json),
                };

                udp.SendToAllClients(message, LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
            catch (Exception ex)
            {
                logger.LogError($"Sending the checkpoint to clients failed: {ex}");
            }
        }
    }
}
