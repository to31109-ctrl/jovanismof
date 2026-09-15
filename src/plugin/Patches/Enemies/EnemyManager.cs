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

        /// <summary>
        /// Set while the mod asks the game its own question, so the answer below is the game's
        /// and not the mod's. Without it the balance service asked how many enemies one player
        /// is allowed, and got back the number this patch had just made up.
        /// </summary>
        internal static bool AskingTheGameDirectly;
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

            // Two things are allowed past the crowd limit, and neither may be allowed past what
            // the game actually allocated. Overrunning the pool does not refuse politely -- it
            // hands back an enemy that is still alive and in play, which is then torn out of the
            // fight and rebuilt somewhere else. That is what "the mobs fly around" is.
            var ceiling = gameBalanceService.GetPooledEnemyCeiling();

            // The revive ghost goes through this same spawner. Refusing it because the map is
            // full is how a downed player ends up with no ghost at all and no way back.
            //
            // But this used to wave through *every* spawn for as long as a coffin existed, not
            // just the ghost -- so the entire time somebody was down, nothing was limited at all.
            // "When those ghosts spawn it gets rough" was that: a downed player quietly switched
            // the enemy limit off for everyone.
            if (Plugin.Instance != null && Plugin.Instance.CurrentReviver.HasValue)
            {
                return __instance.numEnemies < ceiling;
            }

            // A boss is the stage, not part of the crowd. Counting it against the same limit as
            // trash means a full map silently refuses to spawn it, and the fight the players
            // walked into never begins.
            if (flag == EEnemyFlag.Boss || flag == EEnemyFlag.FinalBoss)
            {
                return __instance.numEnemies < ceiling;
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
        /// Tells the game the limit is whatever this lobby actually allows, so enemies do not go
        /// passive once the map is fuller than the game expects.
        ///
        /// This used to answer a flat 1000 whatever the session was doing. That existed because
        /// the mod raised the mob count far above the game's own limit and the game responded by
        /// making enemies less aggressive. The mob count now follows the game's own number, and
        /// when those two agree there is nothing to correct: the answer is the game's own and the
        /// session behaves exactly like single player. Only a lobby that has deliberately raised
        /// the limit is told anything different, and then only the truth about its own limit.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyManager.GetNumMaxEnemies))]
        public static void GetNumMaxEnemies(ref int __result, EnemyManager __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            // Asked by the mod rather than by the game: answer honestly, and do not recurse.
            if (AskingTheGameDirectly) return;

            try
            {
                var allowed = gameBalanceService.GetMaxEnemiesSpawnable();
                if (allowed > __result) __result = allowed;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not work out this lobby's mob limit: {ex.Message}");
            }
        }
    }
}
