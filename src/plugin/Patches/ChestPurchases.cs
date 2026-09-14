using Assets.Scripts.Inventory__Items__Pickups.Chests;
using Assets.Scripts.Inventory__Items__Pickups.Interactables;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(InteractableChest))]
    internal static class ChestPurchases
    {
        // Only the buyer pays. Peers replaying the shared reward must not purchase it again.
        internal static InteractableChest SharedRewardChest;

        [HarmonyPostfix, HarmonyPatch(nameof(InteractableChest.GetPrice))]
        private static void SharedPrice(InteractableChest __instance, ref int __result)
        {
            if (SharedRewardChest != null && __instance == SharedRewardChest) __result = 0;
        }

        [HarmonyPostfix, HarmonyPatch(nameof(InteractableChest.CanAfford))]
        private static void CanAfford(InteractableChest __instance, ref bool __result)
        {
            if (!Plugin.Services.GetRequiredService<ISynchronizationService>().HasNetplaySessionStarted()) return;
            if (__instance.chestType != EChest.Normal && __instance.chestType != EChest.Corrupt) return;
            var inventory = GameManager.Instance?.player?.inventory;
            if (inventory == null) return;

            // Only ever widens the answer, and only by the rounding between the number on the
            // HUD and the wallet behind it, so a chest costing exactly what is displayed can be
            // bought. Forcing it true on a wallet that genuinely cannot cover the price lets the
            // game charge it anyway and take the player into debt.
            var price = __instance.GetPrice();
            if (!__result && inventory.goldInt >= price && inventory.gold > price - 1f) __result = true;
        }
    }
}
