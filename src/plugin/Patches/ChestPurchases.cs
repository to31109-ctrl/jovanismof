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

        /// <summary>
        /// While the game's clock is below this, a debit on the local wallet is a replayed
        /// one, not a purchase, and is skipped.
        ///
        /// When one player buys a chest, every other player replays that interaction on their
        /// own copy of the chest so they can share the reward. That replay is only ever meant
        /// to hand out the item -- but the game charges the local wallet for it at the local
        /// price, which climbs with every chest. A session's logs showed both clients charged
        /// nineteen times each, up to 26,667g apiece, for chests they never bought, while the
        /// host kept everything. The buyer pays on their own machine; nobody else pays anywhere.
        ///
        /// A clock rather than a flag, so nothing has to be cleared for it to end: twenty
        /// seconds covers the replay and the window it opens, and then it is simply gone.
        /// </summary>
        internal static float ReplayShieldUntil;

        /// <summary>
        /// While the game's clock is below this, this player pressed the interact key themselves
        /// and anything they are charged is genuinely theirs to pay.
        ///
        /// Without this the shield above was blanket: for twenty seconds after anybody else's
        /// purchase, nothing cost this player anything. With three players opening chests those
        /// twenty seconds almost never lapse, so gold stopped meaning anything at all. Their own
        /// press is what tells the two apart, and it wins.
        /// </summary>
        internal static float OwnPurchaseUntil;

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
