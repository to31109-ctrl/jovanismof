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
        public static bool ChangeGold_Prefix(PlayerInventory __instance, int amount, out int __state)
        {
            __state = __instance.goldInt;

            // A replayed interaction must never take gold off a player who did not make it.
            // Only the buyer pays, on their own machine; everyone else replays the interaction
            // for the reward alone. Skipped whole, so the wallet, the HUD and the network never
            // see a debit that was never a purchase.
            if (amount < 0
                && __instance == GameManager.Instance?.player?.inventory
                && synchronizationService.HasNetplaySessionStarted()
                && UnityEngine.Time.unscaledTime < ChestPurchases.ReplayShieldUntil)
            {
                Plugin.Log.LogWarning($"Blocked a replayed debit of {-amount}g on a wallet holding {__instance.goldInt}g; only the buyer pays.");
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerInventory.ChangeGold))]
        public static void ChangeGold_Postfix(PlayerInventory __instance, int amount, int __state)
        {
            // A wallet below zero is never a state the player can act on: everything they try
            // to buy is unaffordable until they have earned the debt back, which reads as gold
            // being stolen. Clamped here rather than at each spender, so no path can produce it.
            // Written through the backing fields so this does not re-enter ChangeGold.
            if (__instance != null && __instance.goldInt < 0)
            {
                Plugin.Log.LogWarning($"Gold went negative ({__instance.goldInt}) after a change of {amount}; clamped to zero.");
                __instance._gold_k__BackingField = 0f;
                __instance._goldInt_k__BackingField = 0;
            }

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
