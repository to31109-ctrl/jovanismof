using Assets.Scripts.UI.InGame.Rewards;
using HarmonyLib;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// Used to keep a level-up choice on screen until an offer was actually taken.
    ///
    /// That made sense when the whole party was frozen waiting: dismissing your choice early
    /// left everyone else holding a frozen world for a pick that could no longer be made. The
    /// world is slowed for a choice now instead of stopped, so nobody waits on anybody and
    /// there is nothing to protect. Worse, refusing to close also trapped windows where no
    /// offer is ever "taken", which read as the player's keys dying mid-run. Closing is always
    /// allowed now.
    /// </summary>
    [HarmonyPatch(typeof(BaseEncounterWindow))]
    internal static class ChoiceWindowKeys
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(BaseEncounterWindow.OnClose))]
        private static bool OnClose_Prefix()
        {
            // Nothing waits on a choice any more, so nothing is gained by refusing to close
            // one. Let the game close it.
            return true;
        }
    }
}
