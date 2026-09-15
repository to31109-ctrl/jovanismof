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

            if (synchronizationService.IsSharedExperienceEnabled())
            {
                return true;
            }

            return false;
        }

    }
}
