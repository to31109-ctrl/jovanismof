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
        private static readonly MegabonkTogether.Services.ISynchronizationService synchronizationService =
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetService<MegabonkTogether.Services.ISynchronizationService>(Plugin.Services);

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BaseEncounterWindow.OnClose))]
        private static bool OnClose_Prefix()
        {
            // Nothing waits on a choice any more, so nothing is gained by refusing to close
            // one. Let the game close it.
            return true;
        }

        /// <summary>
        /// Takes the choice off the keyboard the instant it appears.
        ///
        /// Unity treats space and enter as "submit" on whichever element is selected, and a
        /// fresh window selects its skip button -- so a tap of the jump key threw the upgrade
        /// away. There is a sweep every frame that clears the selection, but it cannot run
        /// before the frame the window opens on, and that one frame was enough: this was
        /// reported as still happening after that sweep was added.
        ///
        /// It matters more now, not less. The world keeps running during a choice, so the
        /// player is moving and jumping while the window is up rather than standing frozen in
        /// front of it. The mouse is untouched; only submit-by-keyboard has nothing to press.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(BaseEncounterWindow.Open))]
        private static void Open_Postfix()
        {
            try
            {
                // Co-op only. On your own there is nobody to be inconvenienced by a mis-press,
                // and taking the window off the keyboard would stop anyone playing with a
                // controller or the arrow keys from choosing at all.
                if (!synchronizationService.HasNetplaySessionStarted()) return;

                var events = UnityEngine.EventSystems.EventSystem.current;
                if (events != null && events.currentSelectedGameObject != null)
                {
                    events.SetSelectedGameObject(null);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not take the choice off the keyboard as it opened: {ex.Message}");
            }
        }
    }
}
