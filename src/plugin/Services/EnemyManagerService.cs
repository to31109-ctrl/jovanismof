using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Extensions;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Enemies;
using MonoMod.Utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MegabonkTogether.Services
{
    public interface IEnemyManagerService
    {
        public IEnumerable<(uint, uint)> ReTargetEnemies(uint oldTargetId, IEnumerable<uint> currentPlayersExcludingOldOneId);
        public IEnumerable<EnemyModel> GetAllEnemiesDeltaAndUpdate();
        // BonkLink edition, 2026-09-13: co-op world checkpoints read and rebuild the live enemy set.
        public IEnumerable<KeyValuePair<uint, Enemy>> GetAllSpawnedEnemies();
        public void ReserveEnemyIds(uint highestUsedId);
        public uint AddSpawnedEnemy(Enemy enemy);
        public void SetSpawnedEnemy(uint enemyId, Enemy enemy);
        public Enemy GetEnemyById(uint id);
        public KeyValuePair<uint, Enemy> GetEnemyByReference(Enemy enemy);
        public void RemoveEnemyById(uint id);
        public void ResetForNextLevel();
        public void ApplyRetargetedEnemies(IEnumerable<(uint, uint)> enemy_NewTargetids, IEnumerable<(uint, Rigidbody)> playerId_rigidbody);
        public void InitializeSwitcher(TargetSwitcher switcher, EEnemyFlag enemyFlag, EEnemy enemyName);

        public void AddReviverEnemy_Name(Enemy enemy, string netplayName);
        public string GetReviverEnemy_Name(Enemy enemy);
        public void RemoveReviverEnemy_Name(Enemy enemy);
        public void RebalanceIfNeededReviverEnemy(Enemy enemy, uint? currentReviver, uint? currentReviverOwner);
        public void ResetReviverSpawnCounts();
    }
    internal class EnemyManagerService : IEnemyManagerService
    {
        private readonly ConcurrentDictionary<uint, Enemy> spawnedEnemies = [];
        // BonkLink edition, 2026-09-12: compare against sent state and periodically repair packet loss.
        private readonly MegabonkTogether.Common.Networking.SnapshotBaseline<uint, EnemyModel> enemyBaseline = new();
        private double nextFullSnapshot;
        private readonly ConcurrentDictionary<Enemy, string> reviverEnemies_NetplayNames = [];
        private readonly ConcurrentDictionary<uint, int> reviverSpawnCountPerOwner = [];
        private uint currentEnemyId = 0; //TODO: concurrency?

        private const float POSITION_TRESHOLD = 0.1f;
        private const float YAW_TRESHOLD = 5.0f;
        private const ushort HP_TRESHOLD = 1;

        /// <summary>
        /// Server side, retarget enemies when a player dies (or other use case ? )
        /// </summary>
        public IEnumerable<(uint, uint)> ReTargetEnemies(uint oldTargetId, IEnumerable<uint> currentPlayersAliveExcludingOldOneId)
        {
            var retargetedEnemies = new List<(uint, uint)>();

            // BonkLink edition, 2026-09-13: when the last living player dies there is nobody left
            // to hand the enemies to. Picking a target from an empty set threw out of the native
            // death callback and left the rest of the death handling unfinished.
            var candidates = currentPlayersAliveExcludingOldOneId?.ToArray() ?? [];
            if (candidates.Length == 0)
            {
                return retargetedEnemies;
            }

            var oldTargetEnemies = spawnedEnemies.Values.Where(enemy =>
            {
                var currentTargetid = DynamicData.For(enemy).Get<uint?>("targetId");
                if (currentTargetid.HasValue && currentTargetid.Value == oldTargetId)
                {
                    return true;
                }
                return false;
            });


            foreach (var oldEnemy in oldTargetEnemies)
            {
                var randomNewTargetId = candidates[Random.Range(0, candidates.Length)];

                DynamicData.For(oldEnemy).Set("targetId", randomNewTargetId);
                var enemyId = GetEnemyByReference(oldEnemy).Key;

                retargetedEnemies.Add((enemyId, randomNewTargetId));
            }

            return retargetedEnemies;
        }

        public void ApplyRetargetedEnemies(IEnumerable<(uint, uint)> enemy_NewTargetids, IEnumerable<(uint, Rigidbody)> playerId_rigidbody)
        {
            foreach (var (enemyId, newTargetId) in enemy_NewTargetids)
            {
                var enemy = GetEnemyById(enemyId);
                if (enemy != null)
                {
                    var playerRigidbody = playerId_rigidbody.FirstOrDefault(pr => pr.Item1 == newTargetId).Item2;
                    if (playerRigidbody != null)
                    {
                        DynamicData.For(enemy).Set("targetId", newTargetId);
                        enemy.target = playerRigidbody;
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"Failed to retarget enemy {enemyId} to new target {newTargetId} - enemy not found");
                }
            }
        }

        /// <summary>
        /// This should be called once per server tick
        /// </summary>
        /// <returns></returns>
        public IEnumerable<EnemyModel> GetAllEnemiesDeltaAndUpdate()
        {
            var currentEnemies = new Dictionary<uint, EnemyModel>(spawnedEnemies.Count);
            foreach (var (id, enemy) in spawnedEnemies)
            {
                currentEnemies[id] = enemy.ToModel(id);
            }

            var now = Time.realtimeSinceStartupAsDouble;
            bool refresh = now >= nextFullSnapshot;
            if (refresh) nextFullSnapshot = now + 1.0;
            return enemyBaseline.Collect(currentEnemies, HasDelta, refresh);
        }

        private bool HasDelta(EnemyModel previous, EnemyModel current)
        {
            float positionDelta = Vector3.Distance(
                Quantizer.Dequantize(previous.Position),
                Quantizer.Dequantize(current.Position)
            );

            float yawDelta = Mathf.Abs(
                Quantizer.DequantizeYaw(previous.Yaw)
                - Quantizer.DequantizeYaw(current.Yaw)
            );

            float hpDelta = Mathf.Abs(previous.Hp - current.Hp);

            return positionDelta > POSITION_TRESHOLD ||
                   yawDelta > YAW_TRESHOLD ||
                   hpDelta >= HP_TRESHOLD;
        }

        /// <summary>Every enemy the host currently owns, as a stable snapshot safe to enumerate.</summary>
        public IEnumerable<KeyValuePair<uint, Enemy>> GetAllSpawnedEnemies() => spawnedEnemies.ToArray();

        /// <summary>
        /// Keeps newly spawned enemies from reusing an id that a restored checkpoint already holds.
        /// </summary>
        public void ReserveEnemyIds(uint highestUsedId)
        {
            if (highestUsedId > currentEnemyId)
            {
                currentEnemyId = highestUsedId;
            }
        }

        public Enemy GetEnemyById(uint id)
        {
            if (spawnedEnemies.TryGetValue(id, out var enemy))
            {
                return enemy;
            }
            return null;
        }


        /// <summary>
        /// Server side
        /// </summary>
        public uint AddSpawnedEnemy(Enemy enemy)
        {
            currentEnemyId++;
            if (!spawnedEnemies.TryAdd(currentEnemyId, enemy))
            {
                Plugin.Log.LogWarning($"Attempted to add an enemy that already exists. EnemyId: {currentEnemyId}");
                return 0;
            }

            DynamicData.For(enemy).Set("netplayId", currentEnemyId);

            return currentEnemyId;
        }

        /// <summary>
        /// Client side
        /// </summary>
        public void SetSpawnedEnemy(uint enemyId, Enemy enemy)
        {
            if (!spawnedEnemies.TryAdd(enemyId, enemy))
            {
                Plugin.Log.LogWarning($"Attempted to add an enemy that already exists. EnemyId: {enemyId}");
                return;
            }

            DynamicData.For(enemy).Set("netplayId", enemyId);
        }

        public KeyValuePair<uint, Enemy> GetEnemyByReference(Enemy enemy)
        {
            var netplayId = DynamicData.For(enemy).Get<uint?>("netplayId");
            if (netplayId.HasValue && spawnedEnemies.TryGetValue(netplayId.Value, out var stored) && stored == enemy)
            {
                return new KeyValuePair<uint, Enemy>(netplayId.Value, stored);
            }

            return spawnedEnemies.FirstOrDefault(kv => kv.Value == enemy);
        }

        public void RemoveEnemyById(uint id)
        {
            if (!spawnedEnemies.TryRemove(id, out var enemy))
            {
                return;
            }
        }

        public void ResetForNextLevel()
        {
            //spawnedEnemies.Select(Enemy => Enemy.Value).ToList().ForEach(enemy => GameObject.Destroy(enemy.gameObject));
            spawnedEnemies.Clear();
            enemyBaseline.Clear();
            nextFullSnapshot = 0;
        }

        //TODO: the applied values should be stored in GameBalanceService
        public void InitializeSwitcher(TargetSwitcher switcher, EEnemyFlag enemyFlag, EEnemy enemyName)
        {
            switch (enemyName)
            {
                case EEnemy.GhostGrave1:
                case EEnemy.GhostGrave2:
                case EEnemy.GhostGrave3:
                case EEnemy.GhostGrave4:
                    switcher.UpdateSwitchIntervalRange(7f, 12f);
                    switcher.UpdateSwitchMaxDistance(30f);
                    return;
                default:
                    break;
            }

            switch (enemyFlag)
            {
                case EEnemyFlag.FinalBoss:
                    switcher.UpdateSwitchIntervalRange(30f, 50f);
                    switcher.UpdateSwitchMaxDistance(300f);
                    break;
                case EEnemyFlag.StageBoss:
                    switcher.UpdateSwitchIntervalRange(20f, 40f);
                    switcher.UpdateSwitchMaxDistance(300f);
                    break;
                default:
                    switcher.UpdateSwitchIntervalRange(40f, 60f);
                    switcher.UpdateSwitchMaxDistance(50f);
                    break;
            }
        }

        public void AddReviverEnemy_Name(Enemy enemy, string netplayName)
        {
            reviverEnemies_NetplayNames.TryAdd(enemy, netplayName);
        }

        public string GetReviverEnemy_Name(Enemy enemy)
        {
            if (reviverEnemies_NetplayNames.TryGetValue(enemy, out var name))
            {
                return name;
            }
            return null;
        }

        public void RemoveReviverEnemy_Name(Enemy enemy)
        {
            reviverEnemies_NetplayNames.TryRemove(enemy, out _);
        }

        public void RebalanceIfNeededReviverEnemy(Enemy enemy, uint? currentReviver, uint? currentReviverOwner)
        {
            if (!currentReviver.HasValue || !currentReviverOwner.HasValue)
            {
                return;
            }

            var ownerId = currentReviverOwner.Value;
            var count = reviverSpawnCountPerOwner.AddOrUpdate(ownerId, 1, (_, prev) => prev + 1);

            // Each death costs the party more, but it always stays a fraction of the enemy it
            // is built from. The old rule stopped reducing at the sixth death and left a full
            // boss standing between the party and their friend; because the ghost is spawned
            // with the boss flag it also carries boss and player-count health scaling, so
            // "full" was several times what anyone expected to have to chew through.
            var ceiling = System.Math.Max(0.01f, Configuration.ModConfig.ReviveGhostHealthPercent.Value / 100f);
            var share = System.Math.Min(count, 6) / 6f;
            var multiplier = ceiling * share;

            var newHp = enemy.hp * multiplier;
            if (!float.IsFinite(newHp) || newHp <= 0) return;

            enemy.hp = newHp;
            enemy.controlHp = newHp;
            enemy.maxHp = newHp;
            enemy._hp_k__BackingField = newHp;
        }

        public void ResetReviverSpawnCounts()
        {
            reviverSpawnCountPerOwner.Clear();
        }
    }
}
