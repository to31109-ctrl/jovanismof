using Assets.Scripts.Utility;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(MyTime))]
    internal static class MyTimePatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// No pause during netplay when no shared experience
        /// </summary>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyTime.Pause))]
        public static bool Pause_Postfix()
        {
            // The host holding the game for a player who is rejoining. Refusing this would
            // make the button do nothing in exactly the session it exists for.
            if (CoopPause.Held) return true;

            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            // Shared sessions used to be allowed to stop the world so everybody could be held
            // on a level-up choice. That is the single thing this mod has got wrong most often:
            // a stopped world is a world somebody can be stranded in, and every fix for it has
            // been another way of noticing the player is stuck. The world now slows for a choice
            // instead -- see ChoiceSlowMotion -- and nothing stops it.
            return false;
        }

    }
}
