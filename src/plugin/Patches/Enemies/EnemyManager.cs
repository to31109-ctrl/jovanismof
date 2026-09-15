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
        public static bool SpawnEnemy_Prefix(bool forceSpawn, EEnemyFlag flag, EnemyManager __instance)
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

            // The revive ghost goes through this same spawner. Refusing it because the map is
            // full is how a downed player ends up with no ghost at all and no way back, so it
            // is always let through: it is one enemy, and it is the whole revive mechanic.
            if (Plugin.Instance != null && Plugin.Instance.CurrentReviver.HasValue)
            {
                return true;
            }

            // A boss is the stage, not part of the crowd. Counting it against the same limit as
            // trash means a full map silently refuses to spawn it, and the fight the players
            // walked into never begins.
            if (flag == EEnemyFlag.Boss || flag == EEnemyFlag.FinalBoss)
            {
                return true;
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
                synchronizationService.OnSpawnedEnemy(__result, enemyData.enemyName, pos, waveNumber, forceSpawn, flag, canBeElite, extraSizeMultiplier);
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
