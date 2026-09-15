// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using Actors.Enemies;
using Assets.Scripts._Data.Tomes;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Managers;
using MegabonkTogether.Common.Persistence;
using MegabonkTogether.Configuration;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MegabonkTogether.Persistence
{
    /// <summary>
    /// Reads the live run into a <see cref="WorldSave"/>. Every adapter is individually
    /// guarded: a member the game no longer exposes records a gap in MissingState instead
    /// of aborting the checkpoint, and gaps in run identity clear <see cref="WorldSave.Resumable"/>.
    /// </summary>
    internal static class WorldCapture
    {
        public static WorldSave Capture(
            Guid worldId,
            Guid hostId,
            long revision,
            IPlayerManagerService players,
            IEnemyManagerService enemies,
            IFinalBossOrbManagerService orbs,
            ISpawnedObjectManagerService objects,
            WorldProgress carriedProgress,
            WorldSave previous)
        {
            var save = new WorldSave
            {
                WorldId = worldId,
                HostId = hostId,
                Revision = revision,
                SavedAt = DateTimeOffset.UtcNow,
                GameVersion = Application.version ?? "",
                ModVersion = MyPluginInfo.PLUGIN_VERSION,
                SharedRewards = ModConfig.EnabledSharedExperience.Value,
                Scaling = Plugin.Instance.Mode.Scaling,
                Resumable = true,
                Progress = carriedProgress ?? new WorldProgress(),
            };

            try { save.Seed = players.GetSeed(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"World capture: seed unavailable ({ex.Message})"); save.MissingState.Add("seed"); }

            CaptureRun(save);
            CaptureProgress(save);
            CapturePlayers(save, players, hostId);
            CarryForwardAbsentPlayers(save, previous);
            CaptureEnemies(save, enemies, players);
            CaptureBossOrbs(save, orbs);
            CaptureObjects(save, objects);
            CapturePickups(save);

            if (save.Players.Count == 0 || string.IsNullOrEmpty(save.Stage))
            {
                save.Resumable = false;
            }

            return save;
        }

        private static void CaptureRun(WorldSave save)
        {
            try
            {
                var config = MapController.runConfig;
                if (config == null)
                {
                    save.MissingState.Add("run configuration");
                    save.Resumable = false;
                    return;
                }

                save.Map = (int)config.mapData.eMap;

                // The stage the party is standing in, not the one the run began on.
                //
                // This used to read config.stageData, which is part of the *run* configuration
                // and never changes after the first stage is generated. Every checkpoint of
                // every run therefore claimed to be on the map's first stage: a party at level
                // 59 in the final area saved a world reading "Stage: StageForest1, StageIndex: 1".
                // Loading it then did exactly what it said and put everyone back at the
                // beginning, which is what "loading a world sends us back to the start" was.
                // MapController.currentStage is what the rest of the mod already uses to know
                // where the party is -- GameBalanceService reads it to scale each stage.
                var currentStage = MapController.currentStage;
                save.Stage = currentStage?.name ?? config.stageData?.name ?? "";

                save.Tier = config.mapTierIndex;
                save.Music = config.musicTrackIndex;
                save.Challenge = config.challenge?.name ?? "";

                // Likewise taken from where the party actually is. MapController.index is not
                // the stage number and was the other half of the same mistake.
                var stages = config.mapData?.stages;
                var index = (stages != null && currentStage != null) ? stages.IndexOf(currentStage) : -1;
                save.StageIndex = index >= 0 ? index : MapController.index;

                save.Name = $"{config.mapData.eMap} - {save.Stage}";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: run configuration unavailable ({ex.Message})");
                save.MissingState.Add("run configuration");
                save.Resumable = false;
            }
        }

        private static void CaptureProgress(WorldSave save)
        {
            try
            {
                var manager = GameManager.Instance;
                if (manager == null)
                {
                    save.MissingState.Add("game manager");
                    save.Resumable = false;
                    return;
                }

                save.ElapsedSeconds = Math.Max(0, manager.gameTimer);
                save.Progress.IsCrypt = manager.isCrypt;
                save.Progress.CryptIndex = manager.cryptIndex;
                save.Progress.BossCurses = manager.bossCurses;
                save.Progress.EnteredBossRoom = manager.HasEnteredBossRoom();
                save.Progress.FinalBossDead = manager.IsFinalBossDead();
                save.Progress.DungeonTimerStarted = manager.isDungeonTimerStarted;
                save.Progress.DungeonTimeToComplete = manager.dungeonTimeToComplete;
                save.Progress.DungeonOvertime = manager.isDungeonOvertime;
                save.Progress.FinalSwarm = manager.IsFinalSwarm();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: run progress unavailable ({ex.Message})");
                save.MissingState.Add("run progress");
            }
        }

        private static void CapturePlayers(WorldSave save, IPlayerManagerService players, Guid hostId)
        {
            var seen = new HashSet<uint>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<Guid>();

            foreach (var player in players.GetAllPlayers().ToArray())
            {
                if (player == null || !seen.Add(player.ConnectionId)) continue;

                var isLocal = players.IsLocalConnectionId(player.ConnectionId);
                var inventory = isLocal
                    ? GameManager.Instance?.player?.inventory
                    : players.GetNetPlayerByNetplayId(player.ConnectionId)?.Inventory;

                // The local player knows its own identity from configuration; remote ones report
                // theirs when they enter the game. Copied installations can collide, and a slot
                // that cannot be told apart is better left unkeyed than wrongly handed over.
                var identity = isLocal ? ModConfig.PlayerIdentity.Value ?? "" : player.Identity ?? "";
                if (identity.Length > 0 && !identities.Add(identity))
                {
                    save.MissingState.Add($"unique identity for {player.Name}");
                    identity = "";
                }

                var playerId = isLocal && player.IsHost ? hostId : DeterministicId(identity, player.ConnectionId);
                while (!ids.Add(playerId)) playerId = Guid.NewGuid();

                var saved = new SavedPlayer
                {
                    // The host's slot is keyed to the world so a checkpoint always has exactly one host.
                    PlayerId = playerId,
                    Identity = identity,
                    ConnectionId = player.ConnectionId,
                    Name = player.Name ?? "Player",
                    Character = (int)player.Character,
                    Skin = player.Skin ?? "",
                    IsHost = player.IsHost,
                    Connected = true,
                };

                CapturePose(saved, player, isLocal);
                CaptureInventory(save, saved, inventory, isLocal ? null : player);
                save.Players.Add(saved);
            }

            if (save.Players.Count(p => p.IsHost) != 1)
            {
                save.MissingState.Add("host player record");
                save.Resumable = false;
            }
        }

        /// <summary>
        /// A world remembers everyone who has ever played it. Someone who left earlier keeps their
        /// character exactly as it was, so it is waiting for them whenever they come back.
        /// </summary>
        private static void CarryForwardAbsentPlayers(WorldSave save, WorldSave previous)
        {
            if (previous == null) return;

            // Keyed per character, so switching character preserves the one you stepped away from.
            var present = new HashSet<(string, int)>(save.Players
                .Where(p => !string.IsNullOrEmpty(p.Identity))
                .Select(p => (p.Identity, p.Character)));
            var usedIds = new HashSet<Guid>(save.Players.Select(p => p.PlayerId));

            foreach (var remembered in previous.Players)
            {
                if (remembered == null || string.IsNullOrEmpty(remembered.Identity)) continue;
                if (present.Contains((remembered.Identity, remembered.Character))) continue;
                if (save.Players.Count >= Common.Persistence.WorldSaveStore.MaximumRoster) break;

                // They are not in the run, so nothing about them can be host or connected now.
                remembered.Connected = false;
                remembered.IsHost = false;

                if (!usedIds.Add(remembered.PlayerId))
                {
                    remembered.PlayerId = Guid.NewGuid();
                    usedIds.Add(remembered.PlayerId);
                }

                save.Players.Add(remembered);
            }
        }

        private static void CapturePose(SavedPlayer saved, Common.Models.Player player, bool isLocal)
        {
            try
            {
                if (isLocal)
                {
                    var transform = GameManager.Instance?.player?.transform;
                    if (transform == null) return;

                    var pose = ToPose(transform.position, transform.eulerAngles);
                    if (IsPlausible(pose)) saved.Pose = pose;
                    return;
                }

                // A remote player's model is parked far below the map while they are hidden or
                // dead, so their reported position is the only one worth keeping.
                var reported = Helpers.Quantizer.Dequantize(player.Position);
                reported.y += Plugin.PLAYER_FEET_OFFSET_Y;

                var remotePose = ToPose(reported, Vector3.zero);
                if (IsPlausible(remotePose)) saved.Pose = remotePose;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: player {saved.Name} position unavailable ({ex.Message})");
            }
        }

        /// <summary>A position far outside any stage is a hidden or pooled object, not a place to stand.</summary>
        private static bool IsPlausible(SavedPose pose) =>
            Math.Abs(pose.X) < 10000 && Math.Abs(pose.Y) < 10000 && Math.Abs(pose.Z) < 10000;

        private static void CaptureInventory(WorldSave save, SavedPlayer saved, PlayerInventory inventory, Common.Models.Player reported)
        {
            if (inventory == null)
            {
                save.MissingState.Add($"inventory for {saved.Name}");
                return;
            }

            try
            {
                var health = inventory.playerHealth;
                if (health != null)
                {
                    saved.Hp = health.hp;
                    saved.MaxHp = health.maxHp;
                    saved.Overheal = health.overheal;
                    saved.Shield = health.shield;
                    saved.MaxShield = health.maxShield;
                    saved.Dead = health.IsDead();
                }

                if (reported == null)
                {
                    saved.Gold = inventory.goldInt;
                    saved.Banishes = inventory.banishes;
                    saved.Refreshes = inventory.refreshes;
                    saved.Skips = inventory.skips;

                    var xp = inventory.playerXp;
                    if (xp != null)
                    {
                        saved.Xp = xp.xp;
                        saved.Level = xp.level;
                        saved.LeftOverXp = xp.leftOverXp;
                    }
                }
                else
                {
                    // The host mirrors a remote inventory for display only; gold, experience and
                    // level live on the owning player and reach us in their own updates.
                    saved.Gold = reported.Gold;
                    saved.Xp = reported.Xp;
                    saved.Level = reported.Level;
                    saved.Banishes = reported.Banishes;
                    saved.Refreshes = reported.Refreshes;
                    saved.Skips = reported.Skips;
                    if (float.IsFinite(reported.Overheal)) saved.Overheal = Math.Max(0f, reported.Overheal);
                    if (reported.MaxHp > 0)
                    {
                        saved.Hp = (int)reported.Hp;
                        saved.MaxHp = (int)reported.MaxHp;
                        saved.Shield = reported.Shield;
                        saved.MaxShield = reported.MaxShield;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: vitals for {saved.Name} unavailable ({ex.Message})");
                save.MissingState.Add($"vitals for {saved.Name}");
            }

            // Validation refuses an impossible record, and losing the whole checkpoint over a
            // transient value would be worse than clamping it here.
            saved.MaxHp = Math.Max(0, saved.MaxHp);
            saved.Hp = Math.Max(0, Math.Min(saved.Hp, saved.MaxHp));
            saved.Xp = Math.Max(0, saved.Xp);
            saved.Level = Math.Max(0, saved.Level);
            saved.Shield = Math.Max(0f, saved.Shield);
            saved.Overheal = Math.Max(0f, saved.Overheal);
            saved.Banishes = Math.Max(0, saved.Banishes);
            saved.Refreshes = Math.Max(0, saved.Refreshes);
            saved.Skips = Math.Max(0, saved.Skips);

            CaptureStats(save, saved, inventory, reported);
            CaptureWeapons(save, saved, inventory);
            CaptureTomes(save, saved, inventory);
            CaptureItems(save, saved, inventory);
        }

        /// <summary>
        /// The upgrade a player picked at every level-up, plus shrines, all live here as
        /// permanent stat changes. A restored character that keeps its level number but loses
        /// these is level twenty with a level one body, which is what players saw as their
        /// stats being wiped.
        /// </summary>
        private static void CaptureStats(WorldSave save, SavedPlayer saved, PlayerInventory inventory, Common.Models.Player reported)
        {
            // A remote player's inventory here is a display mirror and holds none of their
            // upgrades, so what they reported themselves is the only real source.
            if (reported != null)
            {
                if (reported.Stats == null || reported.Stats.Count == 0)
                {
                    save.MissingState.Add($"stat upgrades for {saved.Name}");
                    return;
                }

                foreach (var modifier in reported.Stats)
                {
                    if (modifier == null || !float.IsFinite(modifier.Value)) continue;
                    saved.Stats.Add(new SavedModifier { Stat = modifier.Stat, Operation = modifier.Operation, Value = modifier.Value });
                }
                return;
            }

            try
            {
                var permanent = inventory.statInventory?.permanentChanges;
                if (permanent == null)
                {
                    save.MissingState.Add($"stat upgrades for {saved.Name}");
                    return;
                }

                foreach (var entry in permanent)
                {
                    if (entry.Value == null) continue;
                    foreach (var modifier in entry.Value)
                    {
                        if (modifier == null) continue;
                        if (!float.IsFinite(modifier.modification)) continue;
                        saved.Stats.Add(new SavedModifier
                        {
                            Stat = (int)modifier.stat,
                            Operation = (int)modifier.modifyType,
                            Value = modifier.modification,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: stat upgrades for {saved.Name} unavailable ({ex.Message})");
                save.MissingState.Add($"stat upgrades for {saved.Name}");
            }
        }

        private static void CaptureWeapons(WorldSave save, SavedPlayer saved, PlayerInventory inventory)
        {
            try
            {
                var weapons = inventory.weaponInventory?.weapons;
                if (weapons == null) return;

                foreach (var key in weapons.Keys)
                {
                    var weapon = weapons[key];
                    if (weapon == null) continue;

                    var upgrade = new SavedUpgrade
                    {
                        Kind = SavedUpgrade.WeaponKind,
                        Type = (int)key,
                        Level = weapon.level,
                        Enabled = weapon.enabled,
                    };

                    // The game keeps the full per-level modifier history on the weapon, so a
                    // restored weapon can be rebuilt by replaying exactly what was taken.
                    var history = weapon.upgrades;
                    if (history != null)
                    {
                        for (var level = 0; level < history.Count; level++)
                        {
                            upgrade.Levels.Add(ToModifierSet(history[level]));
                        }
                    }

                    saved.Upgrades.Add(upgrade);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: weapons for {saved.Name} unavailable ({ex.Message})");
                save.MissingState.Add($"weapons for {saved.Name}");
            }
        }

        private static void CaptureTomes(WorldSave save, SavedPlayer saved, PlayerInventory inventory)
        {
            try
            {
                var tomes = inventory.tomeInventory;
                if (tomes?.tomeLevels == null) return;

                foreach (var key in tomes.tomeLevels.Keys)
                {
                    var level = tomes.tomeLevels[key];
                    if (level <= 0) continue;

                    var upgrade = new SavedUpgrade
                    {
                        Kind = SavedUpgrade.TomeKind,
                        Type = (int)key,
                        Level = level,
                        Rarity = MonoMod.Utils.DynamicData.For(tomes).Get<int?>($"bonklink.tomeRarity.{(int)key}") ?? 0,
                    };

                    // Tomes keep one accumulated modifier rather than a per-level history,
                    // so the restore replays that total and then sets the level directly.
                    if (tomes.tomeUpgrade != null && tomes.tomeUpgrade.ContainsKey(key))
                    {
                        upgrade.Levels.Add(ToModifierSet(tomes.tomeUpgrade[key]));
                    }

                    saved.Upgrades.Add(upgrade);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: tomes for {saved.Name} unavailable ({ex.Message})");
                save.MissingState.Add($"tomes for {saved.Name}");
            }
        }

        private static void CaptureItems(WorldSave save, SavedPlayer saved, PlayerInventory inventory)
        {
            try
            {
                var items = inventory.itemInventory?.items;
                if (items == null) return;

                foreach (var key in items.Keys)
                {
                    var item = items[key];
                    if (item == null || item.amount <= 0) continue;
                    saved.Items[(int)key] = item.amount;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: items for {saved.Name} unavailable ({ex.Message})");
                save.MissingState.Add($"items for {saved.Name}");
            }
        }

        private static void CaptureEnemies(WorldSave save, IEnemyManagerService enemies, IPlayerManagerService players)
        {
            try
            {
                foreach (var (id, enemy) in enemies.GetAllSpawnedEnemies())
                {
                    if (enemy == null) continue;

                    Enemy live;
                    try { live = enemy; if (live.gameObject == null) continue; }
                    catch { continue; } // already released back to the pool

                    var saved = new SavedEnemy
                    {
                        NetworkId = id,
                        Species = (int)live.enemyData.enemyName,
                        Flags = (int)live.enemyFlag,
                        Wave = live.waveNumber,
                        Hp = live.hp,
                        MaxHp = live.maxHp,
                        Armor = live.armorCurrent,
                        ArmorMax = live.armorMax,
                        SpeedMultiplier = SaneMultiplier(live.speedMultiplier),
                        SizeMultiplier = 1f,
                        IsBoss = live.IsBoss(),
                        IsFinalBoss = live.IsFinalBoss(),
                        IsElite = live.IsElite(),
                        Pose = ToPose(live.transform.position, live.transform.eulerAngles),
                        ReviverFor = enemies.GetReviverEnemy_Name(live) ?? "",
                    };

                    saved.TargetConnectionId = TargetOf(live);
                    EnemyEffects.Capture(live, saved);
                    var attacks = live.specialAttackController;
                    if (attacks?.cooldowns != null)
                        foreach (var attack in attacks.cooldowns.Keys)
                            saved.AttackCooldowns[attack.attackName] = Math.Max(0f, Finite(attacks.cooldowns[attack] - Time.time));
                    save.Enemies.Add(saved);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: enemies unavailable ({ex.Message})");
                save.MissingState.Add("enemies");
            }
        }

        private static void CaptureBossOrbs(WorldSave save, IFinalBossOrbManagerService orbs)
        {
            try
            {
                foreach (var orb in orbs.GetAllOrbs())
                {
                    if (orb == null) continue;
                    var position = Helpers.Quantizer.Dequantize(orb.Position);
                    save.BossOrbs.Add(new SavedBossOrb
                    {
                        NetworkId = orb.Id,
                        Hp = 0,
                        Pose = ToPose(position, Vector3.zero),
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: boss orbs unavailable ({ex.Message})");
                save.MissingState.Add("boss orbs");
            }
        }

        /// <summary>
        /// The chests, shrines and other interactables still standing. Used objects are recorded
        /// separately as they are consumed, because a rebuilt stage spawns them all again.
        /// </summary>
        private static void CaptureObjects(WorldSave save, ISpawnedObjectManagerService objects)
        {
            try
            {
                foreach (var (id, obj) in objects.GetAllSpawnedObjects())
                {
                    if (obj == null) continue;

                    save.Objects.Add(new SavedObject
                    {
                        NetworkId = id,
                        Prefab = Services.SynchronizationService.PrefabNameOf(obj),
                        Active = obj.activeSelf,
                        Pose = ToPose(obj.transform.position, obj.transform.eulerAngles),
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World capture: world objects unavailable ({ex.Message})");
                save.MissingState.Add("world objects");
            }
        }

        private static void CapturePickups(WorldSave save)
        {
            try
            {
                var pickups = Plugin.Services.GetRequiredService<IPickupManagerService>();
                foreach (var model in pickups.GetAllPickups())
                {
                    var pickup = pickups.GetPickupById(model.Id);
                    if (pickup == null || pickup.pickedUp || !pickup.gameObject.activeInHierarchy) continue;
                    save.Pickups.Add(new SavedPickup
                    {
                        Kind = (int)pickup.ePickup,
                        Value = Math.Max(0, pickup.GetValue()),
                        ReadyDelay = Math.Max(0f, Finite(pickup.readyForPickupTime - Time.time)),
                        Pose = ToPose(pickup.transform.position, pickup.transform.eulerAngles),
                    });
                }
            }
            catch (Exception ex)
            {
                save.MissingState.Add("ground pickups");
                Plugin.Log.LogWarning($"World capture: pickups unavailable ({ex.Message})");
            }
        }

        private static uint TargetOf(Enemy enemy)
        {
            try
            {
                var target = MonoMod.Utils.DynamicData.For(enemy).Get<uint?>("targetId");
                return target ?? 0;
            }
            catch { return 0; }
        }

        private static float SaneMultiplier(float value) => float.IsFinite(value) && value > 0 ? value : 1f;

        private static SavedModifierSet ToModifierSet(Il2CppSystem.Collections.Generic.List<Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier> modifiers)
        {
            var set = new SavedModifierSet();
            if (modifiers == null) return set;

            for (var index = 0; index < modifiers.Count; index++)
            {
                var modifier = modifiers[index];
                if (modifier == null) continue;
                set.Modifiers.Add(ToModifier(modifier));
            }

            return set;
        }

        private static SavedModifierSet ToModifierSet(Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier modifier)
        {
            var set = new SavedModifierSet();
            if (modifier != null) set.Modifiers.Add(ToModifier(modifier));
            return set;
        }

        private static SavedModifier ToModifier(Assets.Scripts.Inventory__Items__Pickups.Stats.StatModifier modifier) => new()
        {
            Stat = (int)modifier.stat,
            Operation = (int)modifier.modifyType,
            Value = float.IsFinite(modifier.modification) ? modifier.modification : 0f,
        };

        private static SavedPose ToPose(Vector3 position, Vector3 euler) => new()
        {
            X = Finite(position.x),
            Y = Finite(position.y),
            Z = Finite(position.z),
            Pitch = Finite(euler.x),
            Yaw = Finite(euler.y),
            Roll = Finite(euler.z),
        };

        private static float Finite(float value) => float.IsFinite(value) ? value : 0f;

        /// <summary>
        /// A stable per-player key. Installations that predate the identity field fall back to
        /// their connection id, which is still unique inside one checkpoint.
        /// </summary>
        internal static Guid DeterministicId(string identity, uint connectionId)
        {
            if (Guid.TryParse(identity, out var parsed) && parsed != Guid.Empty) return parsed;

            var bytes = new byte[16];
            BitConverter.GetBytes(connectionId).CopyTo(bytes, 0);
            bytes[15] = 0x01;
            return new Guid(bytes);
        }
    }
}
