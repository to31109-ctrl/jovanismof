using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Managers;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace MegabonkTogether.Patches.Enemies
{
    [HarmonyPatch(typeof(EnemyManager))]
    internal class EnemyManagerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IEnemyManagerService enemyManagerService = Plugin.Services.GetService<IEnemyManagerService>();
        private static readonly IGameBalanceService gameBalanceService = Plugin.Services.GetService<IGameBalanceService>();
        private static bool spawningExtra;
        private static float extraSpawnRemainder;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyManager.SpawnEnemy), [typeof(EnemyData), typeof(int), typeof(bool), typeof(EEnemyFlag), typeof(bool)])]
        public static void SpawnExtraEnemies(EnemyManager __instance, EnemyData enemyData, int summonerId, bool forceSpawn, EEnemyFlag flag, bool useDirectionBias, Enemy __result)
        {
            if (spawningExtra || forceSpawn || __result == null || __result.IsBoss() || __result.IsFinalBoss()
                || !synchronizationService.HasNetplaySessionStarted() || synchronizationService.IsServerMode() != true) return;
            extraSpawnRemainder += gameBalanceService.GetSpawnMultiplier() - 1f;
            var extra = (int)extraSpawnRemainder;
            extraSpawnRemainder -= extra;
            spawningExtra = true;
            try
            {
                for (var i = 0; i < extra && __instance.numEnemies < gameBalanceService.GetMaxEnemiesSpawnable(); i++)
                    __instance.SpawnEnemy(enemyData, summonerId, false, flag, useDirectionBias);
            }
            finally { spawningExtra = false; }
        }

        /// <summary>
        /// Only the server is allowed to spawn
        /// Also manually enforce max enemies limit
        /// </summary>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyManager.SpawnEnemy), [typeof(EnemyData), typeof(int), typeof(bool), typeof(EEnemyFlag), typeof(bool)])]
        public static bool SpawnEnemy_Prefix(bool forceSpawn, EnemyManager __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }

            if (!forceSpawn && __instance.numEnemies >= gameBalanceService.GetMaxEnemiesSpawnable())
            {
                return false;
            }

            return true;
        }

        //[HarmonyPrefix]
        //[HarmonyPatch(nameof(EnemyManager.SpawnEnemy), [typeof(EnemyData), typeof(Vector3), typeof(int), typeof(bool), typeof(EEnemyFlag), typeof(bool)])]
        //public static void SpawnEnemy_Prefix(ref EnemyData enemyData, Vector3 pos, int waveNumber, bool forceSpawn, EEnemyFlag flag, bool canBeElite)
        //{

        //    if (enemyData.enemyName == EEnemy.MinibossPig)
        //    {
        //        enemyData = DataManager.Instance.GetEnemyData(EEnemy.MinibossGolem);
        //    }
        //}


        /// <summary>
        /// Synchronize enemy spawn
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyManager.SpawnEnemy), [typeof(EnemyData), typeof(Vector3), typeof(int), typeof(bool), typeof(EEnemyFlag), typeof(bool), typeof(float)])]
        public static void SpawnEnemy_Postfix(EnemyData enemyData, Vector3 pos, int waveNumber, bool forceSpawn, EEnemyFlag flag, bool canBeElite, float extraSizeMultiplier, Enemy __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (isServer)
            {
                ApplyLobbyHealthScaling(__result, flag);
                synchronizationService.OnSpawnedEnemy(__result, enemyData.enemyName, pos, waveNumber, forceSpawn, flag, canBeElite, extraSizeMultiplier);
            }
        }

        /// <summary>
        /// Applies the host's per-extra-player health scaling. A restored checkpoint overwrites
        /// health immediately afterwards, so a resumed enemy keeps the health it was saved with
        /// rather than being scaled a second time.
        /// </summary>
        private static void ApplyLobbyHealthScaling(Enemy enemy, EEnemyFlag flag)
        {
            try
            {
                var multiplier = gameBalanceService.GetEnemyHpMultiplier(flag);
                if (!float.IsFinite(multiplier) || multiplier <= 1.0001f) return;

                var scaled = enemy.hp * multiplier;
                if (!float.IsFinite(scaled) || scaled <= 0) return;

                enemy.hp = scaled;
                enemy.maxHp = scaled;
                enemy.controlHp = scaled;
                enemy._hp_k__BackingField = scaled;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not scale enemy health for the lobby: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(EnemyManager.SpawnBoss))]
        public static bool SpawnBoss_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// When Max limit is reached, enemies will start being less aggressive.
        /// This is an attempt to prevent that
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyManager.GetNumMaxEnemies))]
        public static void GetNumMaxEnemies(ref int __result, EnemyManager __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            __result = 1000; //Bait the game to keep monster aggressive; TODO: is it really working ?

            //Plugin.Log.LogInfo($"GetNumMaxEnemies: {__instance.numEnemies} / {__result} ");
        }
    }
}
