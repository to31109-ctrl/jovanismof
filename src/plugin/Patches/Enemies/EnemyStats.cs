using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory.Stats;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches.Enemies
{
    [HarmonyPatch(typeof(EnemyStats))]
    internal static class EnemyStatsPatches
    {
        private static readonly IGameBalanceService gameBalanceService = Plugin.Services.GetService<IGameBalanceService>();
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Adjust enemy HP based on netplay configuration.
        ///
        /// This is the single place lobby health scaling happens: two players means twice the
        /// HP, three means three times, and so on. A second application of the same multiplier
        /// used to live on the spawn path, so a party of three faced nine times the health and
        /// scaling never felt like the player count. Do not add another one.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyStats.GetHp))]
        private static void GetHpPostfix(Enemy enemy, ref float __result)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return;
            }

            var multiplier = gameBalanceService.GetEnemyHpMultiplier(enemy.enemyFlag);
            __result *= multiplier;
#if BONKLINK_TESTING
            if (!loggedHpScale && multiplier > 1.0001f)
            {
                loggedHpScale = true;
                Plugin.Log.LogInfo($"BONKLINK_HP_SCALE: base={__result / multiplier:F1} scaled={__result:F1} mult={multiplier:F2} flag={enemy.enemyFlag}");
            }
#endif
        }
#if BONKLINK_TESTING
        private static bool loggedHpScale;
#endif
    }
}
