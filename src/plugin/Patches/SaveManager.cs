using HarmonyLib;
using MegabonkTogether.Common;
using MegabonkTogether.Configuration;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// The game's own saving, which is left alone.
    ///
    /// Upstream blocks it during netplay so a co-op run cannot contaminate a single-player
    /// profile. The cost of that is every unlock earned together being thrown away, which is a
    /// far worse trade than the one it was protecting against -- and it was a setting, so some
    /// players lost everything and others lost nothing with no way to tell which they were.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager))]
    internal static class SaveManagerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetRequiredService<ISynchronizationService>();
        private static readonly IAutoUpdaterService autoUpdaterService = Plugin.Services.GetService<IAutoUpdaterService>();

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveStats))]
        public static bool SaveGame_Prefix()
        {
            // Always. This used to depend on a per-machine setting, and a player whose setting
            // was off lost every character, unlock and coin they earned in co-op -- silently,
            // for ever, and reported four separate times. Nothing about playing together is a
            // reason to throw away the progress the game made. Steam uploads are still blocked,
            // which is what actually protects the leaderboards.
            return true;
        }

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveProgression))]
        public static bool SaveProgression_Prefix()
        {
            // Always. This used to depend on a per-machine setting, and a player whose setting
            // was off lost every character, unlock and coin they earned in co-op -- silently,
            // for ever, and reported four separate times. Nothing about playing together is a
            // reason to throw away the progress the game made. Steam uploads are still blocked,
            // which is what actually protects the leaderboards.
            return true;
        }

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveConfig))]
        public static bool SaveConfig_Prefix()
        {
            // Always. This used to depend on a per-machine setting, and a player whose setting
            // was off lost every character, unlock and coin they earned in co-op -- silently,
            // for ever, and reported four separate times. Nothing about playing together is a
            // reason to throw away the progress the game made. Steam uploads are still blocked,
            // which is what actually protects the leaderboards.
            return true;
        }

        /// <summary>
        /// Prevent saving on netplay sessions unless allowed in config.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.SaveTemp))]
        public static bool SaveTemp_Prefix()
        {
            // Always. This used to depend on a per-machine setting, and a player whose setting
            // was off lost every character, unlock and coin they earned in co-op -- silently,
            // for ever, and reported four separate times. Nothing about playing together is a
            // reason to throw away the progress the game made. Steam uploads are still blocked,
            // which is what actually protects the leaderboards.
            return true;
        }

        /// <summary>
        /// Trigger update when quitting if an update is available
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SaveManager.OnApplicationQuit))]
        public static void OnApplicationQuit_Prefix()
        {
            try
            {
                if (!autoUpdaterService.IsAnUpdateAvailable())
                {
                    Plugin.Log.LogInfo("No updates available to apply.");
                    return;
                }

                Plugin.Log.LogInfo("Update available - launching updater NOW...");
                var pluginDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                autoUpdaterService.LaunchUpdaterOnExit(pluginDirectory);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error when quitting: {ex}");
            }
        }
    }
}
