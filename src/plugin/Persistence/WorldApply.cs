// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using Actors.Enemies;
using Assets.Scripts._Data.Tomes;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Managers;
using MegabonkTogether.Common.Persistence;
using MegabonkTogether.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MegabonkTogether.Persistence
{
    /// <summary>
    /// Writes a checkpoint back into a live run. Restores are always applied silently:
    /// every peer receives the same slots and applies them itself, so replaying them over
    /// the network would double-count shared gold and experience.
    /// </summary>
    internal static class WorldApply
    {
        /// <summary>Host side: rebuild the world around an already loaded stage.</summary>
        public static void ApplyWorld(WorldSave save, IEnemyManagerService enemies)
        {
            if (save == null) return;

            ApplyProgress(save);
            RespawnEnemies(save, enemies);
            foreach (var pickup in save.Pickups)
            {
                var live = PickupManager.Instance.SpawnPickup(
                    (Assets.Scripts.Inventory__Items__Pickups.Pickups.EPickup)pickup.Kind,
                    new Vector3(pickup.Pose.X, pickup.Pose.Y, pickup.Pose.Z), pickup.Value, false, 0f);
                if (live == null) throw new InvalidOperationException($"Could not restore pickup {pickup.Kind}");
                live.SetValue(pickup.Value);
                live.readyForPickupTime = Time.time + pickup.ReadyDelay;
            }
        }

        /// <summary>
        /// Every peer: restore the given slots, matching each to the local player or to a
        /// remote player by connection id.
        /// </summary>
        public static int ApplyPlayers(IEnumerable<SavedPlayer> slots, IPlayerManagerService players)
        {
            var applied = 0;

            foreach (var slot in slots ?? Enumerable.Empty<SavedPlayer>())
            {
                if (slot == null) continue;

                try
                {
                    var isLocal = players.IsLocalConnectionId(slot.ConnectionId);
                    var inventory = isLocal
                        ? GameManager.Instance?.player?.inventory
                        : players.GetNetPlayerByNetplayId(slot.ConnectionId)?.Inventory;

                    if (inventory == null)
                    {
                        Plugin.Log.LogWarning($"World restore: no inventory for {slot.Name} ({slot.ConnectionId}); skipped");
                        continue;
                    }

                    ApplyInventory(inventory, slot, isLocal, players);
                    applied++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"World restore: failed to restore {slot.Name}: {ex}");
                }
            }

            return applied;
        }

        private static void ApplyInventory(PlayerInventory inventory, SavedPlayer slot, bool isLocal, IPlayerManagerService players)
        {
            var couldSend = Plugin.CAN_SEND_MESSAGES;
            Plugin.CAN_SEND_MESSAGES = false;
            Plugin.Instance.SavePlayerInventoryActions();
            var tomeCallbacks = TomeInventory.A_TomeUpgrade;
            var itemAdded = ItemInventory.A_ItemAdded;
            TomeInventory.A_TomeUpgrade = null;
            ItemInventory.A_ItemAdded = null;

            try
            {
                ApplyStats(inventory, slot);
                ApplyWeapons(inventory, slot);
                ApplyTomes(inventory, slot);
                ApplyItems(inventory, slot);
                ApplyVitals(inventory, slot);
                ApplyPose(slot, isLocal, players);
            }
            finally
            {
                ItemInventory.A_ItemAdded = itemAdded;
                TomeInventory.A_TomeUpgrade = tomeCallbacks;
                Plugin.Instance.RestorePlayerInventoryActions();
                Plugin.CAN_SEND_MESSAGES = couldSend;
            }
        }

        /// <summary>
        /// Replays every permanent stat change the player had. Applied before weapons, tomes
        /// and vitals, because maximum health is itself a stat: restoring health first and the
        /// stat that raises it afterwards would leave the player capped at the wrong value.
        /// </summary>
        private static void ApplyStats(PlayerInventory inventory, SavedPlayer slot)
        {
            if (slot.Stats == null || slot.Stats.Count == 0) return;

            var statInventory = inventory.statInventory;
            if (statInventory == null)
            {
                Plugin.Log.LogWarning($"World restore: {slot.Name} has no stat inventory; their upgrades cannot come back");
                return;
            }

            // The character the player respawns into already carries its own starting
            // modifiers. The saved list is the complete set, starting modifiers included, so
            // replaying it on top of them would hand the player everything twice over. Cleared
            // first, the end state is exactly what was saved.
            try { inventory.statInventory.permanentChanges?.Clear(); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World restore: could not clear existing stats for {slot.Name} ({ex.Message}); not replaying, to avoid doubling them");
                return;
            }

            var restored = 0;
            foreach (var modifier in slot.Stats)
            {
                if (modifier == null || !float.IsFinite(modifier.Value)) continue;
                try
                {
                    // permanent, no timeout, and kept out of the shrine log: this is restoring
                    // what the player already had, not handing them a fresh shrine reward.
                    statInventory.ChangeStat(new StatModifier
                    {
                        stat = (Assets.Scripts.Menu.Shop.EStat)modifier.Stat,
                        modifyType = (EStatModifyType)modifier.Operation,
                        modification = modifier.Value,
                    }, true, 0f, false);
                    restored++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"World restore: a stat upgrade for {slot.Name} could not be replayed ({ex.Message})");
                }
            }

            // Recomputed once at the end rather than per modifier, so the player lands with
            // the totals they had rather than whatever the last queued update produced.
            try { inventory.playerStats?.ForceUpdateStats(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"World restore: stats for {slot.Name} did not recompute ({ex.Message})"); }

            Plugin.Log.LogInfo($"World restore: {restored} stat upgrades returned to {slot.Name}");
        }

        private static void ApplyWeapons(PlayerInventory inventory, SavedPlayer slot)
        {
            var weaponInventory = inventory.weaponInventory;
            if (weaponInventory == null) return;

            foreach (var saved in slot.Weapons)
            {
                var weapon = (EWeapon)saved.Type;

                if (!DataManager.Instance.weapons.TryGetValue(weapon, out var data) || data == null)
                {
                    Plugin.Log.LogWarning($"World restore: unknown weapon {saved.Type} for {slot.Name}");
                    continue;
                }

                if (weaponInventory.weapons != null && weaponInventory.weapons.ContainsKey(weapon)) continue;

                // Replaying the recorded levels in order lets the game recompute the weapon's
                // stats exactly as it did when they were taken.
                weaponInventory.AddWeapon(data, ToIl2CppModifiers(saved.Levels.FirstOrDefault()));

                if (!weaponInventory.weapons.TryGetValue(weapon, out var live) || live == null) continue;

                for (var level = 1; level < saved.Levels.Count; level++)
                {
                    live.Upgrade(ToIl2CppModifiers(saved.Levels[level]));
                }

                if (!saved.Enabled)
                {
                    weaponInventory.ToggleWeapon(weapon, false);
                }
            }
        }

        private static void ApplyTomes(PlayerInventory inventory, SavedPlayer slot)
        {
            var tomeInventory = inventory.tomeInventory;
            if (tomeInventory == null) return;

            foreach (var saved in slot.Tomes)
            {
                var tome = (ETome)saved.Type;

                if (!DataManager.Instance.tomeData.TryGetValue(tome, out var data) || data == null)
                {
                    Plugin.Log.LogWarning($"World restore: unknown tome {saved.Type} for {slot.Name}");
                    continue;
                }

                MonoMod.Utils.DynamicData.For(tomeInventory).Set($"bonklink.tomeRarity.{saved.Type}", saved.Rarity);
                if (tomeInventory.tomeLevels != null && tomeInventory.tomeLevels.ContainsKey(tome)) continue;

                // Tomes are stored as one accumulated modifier, so the total is applied once
                // and the level is then set to what the player had actually reached.
                tomeInventory.AddTome(data, ToIl2CppModifiers(saved.Levels.FirstOrDefault()), (ERarity)saved.Rarity);

                if (tomeInventory.tomeLevels != null && saved.Level > 0)
                {
                    tomeInventory.tomeLevels[tome] = saved.Level;
                }
            }
        }

        private static void ApplyItems(PlayerInventory inventory, SavedPlayer slot)
        {
            var itemInventory = inventory.itemInventory;
            if (itemInventory == null) return;

            foreach (var entry in slot.Items)
            {
                var item = (EItem)entry.Key;
                var have = 0;

                try { have = itemInventory.GetAmount(item); } catch { /* unknown item id */ }

                var missing = entry.Value - have;
                if (missing > 0) itemInventory.AddItem(item, missing);
            }
        }

        private static void ApplyVitals(PlayerInventory inventory, SavedPlayer slot)
        {
            var health = inventory.playerHealth;
            if (health != null)
            {
                if (slot.MaxHp > 0) health.maxHp = slot.MaxHp;

                // A checkpoint taken as the party went down would otherwise resume the run with
                // everyone already dead, which is not a run anyone can play. They come back up.
                if (slot.Hp <= 0)
                {
                    health.hp = health.maxHp;
                    Plugin.Log.LogInfo($"World restore: {slot.Name} was down at the checkpoint and comes back at full health");
                }
                else
                {
                    health.hp = Math.Min(slot.Hp, health.maxHp);
                }
                health.shield = Math.Max(0f, slot.Shield);
                health.overheal = Math.Max(0f, slot.Overheal);
            }

            var currentGold = inventory.goldInt;
            if (slot.Gold != currentGold) inventory.ChangeGold(slot.Gold - currentGold);

            var xp = inventory.playerXp;
            if (xp != null)
            {
                xp.xp = Math.Max(0, slot.Xp);
                xp.level = Math.Max(0, slot.Level);
                xp.leftOverXp = Math.Max(0f, slot.LeftOverXp);
            }

            inventory.banishes = Math.Max(0, slot.Banishes);
            inventory.refreshes = Math.Max(0, slot.Refreshes);
            inventory.skips = Math.Max(0, slot.Skips);
        }

        private static void ApplyPose(SavedPlayer slot, bool isLocal, IPlayerManagerService players)
        {
            var position = new Vector3(slot.Pose.X, slot.Pose.Y, slot.Pose.Z);
            if (position == Vector3.zero) return;

            // A checkpoint from an older build can hold a hidden model's position; dropping a
            // player into the void is far worse than letting them start at the spawn point.
            if (Math.Abs(position.x) > 10000 || Math.Abs(position.y) > 10000 || Math.Abs(position.z) > 10000)
            {
                Plugin.Log.LogWarning($"World restore: ignoring an implausible position for {slot.Name}");
                return;
            }

            if (isLocal)
            {
                var player = GameManager.Instance?.player;
                if (player == null) return;

                player.transform.position = position;

                var body = player.GetComponent<Rigidbody>();
                if (body != null) body.position = position;
                return;
            }

            var netPlayer = players.GetNetPlayerByNetplayId(slot.ConnectionId);
            if (netPlayer != null) netPlayer.transform.position = position;
        }

        private static void ApplyProgress(WorldSave save)
        {
            try
            {
                var manager = GameManager.Instance;
                if (manager == null) return;

                manager.gameTimer = (float)Math.Max(0, save.ElapsedSeconds);
                manager.bossCurses = save.Progress.BossCurses;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"World restore: run progress not applied ({ex.Message})");
            }
        }

        private static void RespawnEnemies(WorldSave save, IEnemyManagerService enemies)
        {
            if (save.Enemies.Count == 0) return;

            // Restored ids are reserved first so freshly spawned enemies cannot collide with them.
            enemies.ReserveEnemyIds(save.Enemies.Max(e => e.NetworkId));

            var restored = 0;
            var unknown = 0;
            var refused = 0;

            foreach (var saved in save.Enemies)
            {
                try
                {
                    var data = DataManager.Instance.GetEnemyData((EEnemy)saved.Species);
                    if (data == null)
                    {
                        unknown++;
                        continue;
                    }

                    var position = new Vector3(saved.Pose.X, saved.Pose.Y, saved.Pose.Z);
                    var enemy = EnemyManager.Instance.SpawnEnemy(
                        data,
                        position,
                        saved.Wave,
                        true,
                        (EEnemyFlag)saved.Flags,
                        false,
                        saved.SizeMultiplier);

                    if (enemy == null)
                    {
                        refused++;
                        continue;
                    }

                    // The spawn broadcast carries the enemy's fresh health; the health below
                    // reaches clients through the next enemy snapshot.
                    var hp = Math.Max(1f, saved.Hp);
                    enemy.hp = hp;
                    enemy.controlHp = hp;
                    enemy.maxHp = Math.Max(hp, saved.MaxHp);
                    enemy._hp_k__BackingField = hp;
                    enemy._armorMax_k__BackingField = Math.Max(0, saved.ArmorMax);
                    enemy._armorCurrent_k__BackingField = Math.Clamp(saved.Armor, 0, enemy.armorMax);
                    enemy.speedMultiplier = saved.SpeedMultiplier > 0 && float.IsFinite(saved.SpeedMultiplier)
                        ? saved.SpeedMultiplier : 1f;
                    enemy.transform.eulerAngles = new Vector3(saved.Pose.Pitch, saved.Pose.Yaw, saved.Pose.Roll);
                    EnemyEffects.Apply(enemy, saved);
                    var attacks = enemy.specialAttackController;
                    if (attacks?.attacks != null && attacks.cooldowns != null)
                        foreach (var attack in attacks.attacks)
                            if (saved.AttackCooldowns.TryGetValue(attack.attackName, out var remaining))
                                attacks.cooldowns[attack] = Time.time + remaining;

                    restored++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"World restore: enemy {saved.NetworkId} not restored ({ex.Message})");
                }
            }

            if (restored < save.Enemies.Count)
            {
                Plugin.Log.LogWarning($"World restore: {save.Enemies.Count - restored} enemies were not rebuilt ({unknown} unknown species, {refused} refused by the spawner)");
            }

            Plugin.Log.LogInfo($"World restore: respawned {restored}/{save.Enemies.Count} enemies");
        }

        private static Il2CppSystem.Collections.Generic.List<StatModifier> ToIl2CppModifiers(SavedModifierSet set)
        {
            var list = new Il2CppSystem.Collections.Generic.List<StatModifier>();
            if (set?.Modifiers == null) return list;

            foreach (var modifier in set.Modifiers)
            {
                list.Add(new StatModifier
                {
                    stat = (Assets.Scripts.Menu.Shop.EStat)modifier.Stat,
                    modification = modifier.Value,
                    modifyType = (EStatModifyType)modifier.Operation,
                });
            }

            return list;
        }
    }
}
