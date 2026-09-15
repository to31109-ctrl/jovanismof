using Assets.Scripts.UI.InGame.Levelup;
using Assets.Scripts.UI.InGame.Rewards;
using Assets.Scripts.Utility;
using Coffee.UIExtensions;
using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(EncounterWindows))]
    internal static class EncounterWindowPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IEncounterService encounterService = Plugin.Services.GetService<IEncounterService>();

        /// <summary>
        /// When changing level, the game will try to pop reward from previous stage missed
        /// This can freeze the game as the queue will get modified while iterating
        /// To prevent that, we just make sure the player can move before popping the reward
        /// Also if shared experience, notify end of reward to not block the game for other players
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EncounterWindows.PopReward))]
        public static bool PopReward_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (!GameManager.Instance.player.playerInput.CanInput())
            {
                //Plugin.Log.LogWarning($"Player can't move yet, skipping reward pop for now");
                return false;
            }

            // Cleared here, before the choice window is built. Doing it afterwards closed the
            // choice window along with everything else, which left the world frozen with
            // nothing on screen to pick.
            if (synchronizationService.IsSharedExperienceEnabled()) CloseScreensBlockingAChoice();

            // A dead player has no choice to make. They used to stop their own world and tell
            // everyone they were waiting; nobody waits for anybody now, so this just declines
            // the reward and leaves the run alone.
            if (GameManager.Instance.player.IsDead())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prevent adding an encounter if one is already in progress
        /// This should not only prevent missing some reward but hopefully also random crashes happening with encounter
        /// The encounter will be queued and popped later at LateUpdate
        /// Also if shared experience, notify end of reward to not block the game for other players
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EncounterWindows.AddEncounter))]
        public static bool AddEncounter_Prefix(EncounterWindows __instance, EEncounter rewardWindowType)
        {

            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (__instance.encounterInProgress)
            {
                //Plugin.Log.LogWarning($"Encounter in progress, queueing encounter {rewardWindowType}");
                __instance.rewardQueue.Enqueue(rewardWindowType);
                return false;
            }

            if (GameManager.Instance.player.IsDead())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Clears menus that would sit over a level-up choice. Opened before the freeze, they
        /// stay on screen after it and the player can neither use them nor reach the choice.
        /// </summary>
        internal static void CloseScreensBlockingAChoice()
        {
            try
            {
                // Never while a choice is on screen: CloseAll would take that window with it and
                // leave the player frozen in front of nothing.
                var encounter = UiManager.Instance?.encounterWindows?.activeEncounterWindow;
                if (encounter != null && encounter.gameObject.activeInHierarchy)
                {
                    Plugin.Log.LogInfo("A choice is already on screen; leaving the windows alone.");
                    return;
                }

                if (WindowManager.HasOpenWindow())
                {
                    Plugin.Log.LogInfo($"Closing {WindowManager.GetNumOpenWindows()} open window(s) so this player can make their choice.");
                    WindowManager.CloseAll();
                }

                var pause = UiManager.Instance?.pause;
                if (pause != null && pause.gameObject.activeInHierarchy) pause.Resume();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not clear the screens covering a choice: {ex.Message}");
            }
        }

        /// <summary>
        /// Prevent pause on netplay reward pop (Shady guy and other) on non shared experience
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EncounterWindows.PopReward))]
        public static void PopReward_Postfix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            // The choice window goes up over a world that keeps running, slowed rather than
            // stopped. Whatever the player had open is theirs to close in their own time; it can
            // no longer strand them, because nothing is frozen underneath it.
            UiManager.Instance.encounterWindows?.activeEncounterWindow?.gameObject.SetActive(true);

            MyTime.Unpause();
        }

        /// <summary>
        /// PopReward_Prefix prevent reward pop if player can't move yet.
        /// If we can move now, we should pop previously prevented reward as soon as possible.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EncounterWindows.LateUpdate))]
        public static void LateUpdate_Prefix(EncounterWindows __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (!GameManager.Instance.player.playerInput.CanInput())
            {
                return;
            }

            if (GameManager.Instance.player.IsDead())
            {
                return;
            }

            var currentQueue = __instance.rewardQueue;
            if (currentQueue.Count > 0 && !__instance.encounterInProgress)
            {
                //Plugin.Log.LogInfo($"Pop previously missed reward");
                __instance.PopReward();
            }
        }


        /// <summary>
        /// Taking an upgrade now simply finishes, for everyone, every time.
        ///
        /// This used to hide the player's window, tell the host they were done and put
        /// "Waiting for other player(s) choices..." on screen until the last player had picked.
        /// Holding a party on the slowest member is what stranded people: any choice that failed
        /// to register left everybody else with a frozen world and nothing to click. The world is
        /// slowed while choosing instead, so there is nothing left to release and nobody to wait
        /// for.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(EncounterWindows.RewardFinished))]
        public static void RewardFinished_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            ScreenTextHelper.Clear();
            encounterService.ClearClosedEncounters();
        }
    }
}
