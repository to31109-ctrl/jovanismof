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
            if (inventory != null) __result = inventory.goldInt >= __instance.GetPrice();
        }
    }
}
