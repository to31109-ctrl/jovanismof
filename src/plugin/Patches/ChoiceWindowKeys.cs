using Assets.Scripts.UI.InGame.Rewards;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// Keeps a level-up choice on screen until an offer is actually taken.
    ///
    /// Pressing the jump key over the choice dismissed the window without choosing anything.
    /// In a shared-experience session that is worse than it sounds: the rest of the party is
    /// frozen waiting for a choice that can no longer be made, and the player who pressed it
    /// has nothing left to click.
    /// </summary>
    [HarmonyPatch(typeof(BaseEncounterWindow))]
    internal static class ChoiceWindowKeys
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>Set while the mod closes a window itself, which must always be allowed.</summary>
        internal static bool ClosingDeliberately;

        private static bool offerTaken;

        /// <summary>A fresh window has had nothing chosen from it yet.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(BaseEncounterWindow.Open))]
        private static void Open_Postfix() => offerTaken = false;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(BaseEncounterWindow.ChooseOffer))]
        private static void ChooseOffer_Postfix() => offerTaken = true;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BaseEncounterWindow.OnClose))]
        private static bool OnClose_Prefix()
        {
            // Outside a shared session the game's own behaviour is left alone: nobody else is
            // waiting, so dismissing your own choice only affects you.
            if (!synchronizationService.HasNetplaySessionStarted()) return true;
            if (!synchronizationService.IsSharedExperienceEnabled()) return true;

            if (ClosingDeliberately || offerTaken) return true;

            Plugin.Log.LogInfo("Ignoring a keypress that would have dismissed a choice nobody had made yet.");
            return false;
        }
    }
}
