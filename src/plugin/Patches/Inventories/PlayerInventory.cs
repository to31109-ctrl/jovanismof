using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches.Inventories
{
    [HarmonyPatch(typeof(PlayerInventory))]
    internal static class PlayerInventoryPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// Prevent inventory tick (spawning projectiles) when dead. Only tick status effects.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerInventory.PhysicsTick))]
        public static bool PhysicsTick_Prefix(PlayerInventory __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (GameManager.Instance.player.inventory != null && __instance == GameManager.Instance.player.inventory && GameManager.Instance.player.IsDead())
            {
                __instance.statusEffects.Tick();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Share actual local earnings independently of shared experience.
        /// Spending stays in the buyer's wallet.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerInventory.ChangeGold))]
        public static void ChangeGold_Prefix(PlayerInventory __instance, out int __state)
        {
            __state = __instance.goldInt;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerInventory.ChangeGold))]
        public static void ChangeGold_Postfix(PlayerInventory __instance, int amount, int __state)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (__instance != GameManager.Instance?.player?.inventory)
            {
                return;
            }

            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return;
            }

            if (amount < 0)
            {
                return;
            }

            var earned = (long)__instance.goldInt - __state;
            if (earned > 0) synchronizationService.OnChangeGold((int)System.Math.Min(int.MaxValue, earned));
        }
    }
}
