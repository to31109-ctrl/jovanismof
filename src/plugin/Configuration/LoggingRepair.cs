using System;
using System.IO;
using System.Text;

namespace MegabonkTogether.Configuration
{
    /// <summary>
    /// Makes sure the player actually has a log file.
    ///
    /// The installer switches the console off so nobody plays behind a wall of scrolling text,
    /// and on an installation that already had a BepInEx config it left disk logging at
    /// whatever BepInEx defaulted to -- which is off. Players then had no LogOutput.log at all,
    /// so nothing they reported could be looked into from their own machine.
    ///
    /// Repairing that in the installer reaches nobody who already has the mod: an update
    /// replaces the plugin and never runs the installer. So the plugin repairs it, once.
    /// BepInEx reads this file long before any plugin loads, so the change takes effect on the
    /// next launch rather than this one.
    /// </summary>
    internal static class LoggingRepair
    {
        internal static void EnsureDiskLogging()
        {
            try
            {
                var path = Path.Combine(BepInEx.Paths.ConfigPath, "BepInEx.cfg");
                if (!File.Exists(path)) return;

                var text = File.ReadAllText(path);
                var updated = Common.LoggingConfig.WithDiskLoggingOn(text);
                if (updated == text) return;

                File.WriteAllText(path, updated, new UTF8Encoding(false));
                Plugin.Log.LogInfo("Switched BepInEx disk logging on; a log file will be written from the next launch.");
            }
            catch (Exception ex)
            {
                // A missing log is a nuisance, never a reason to stop the game loading.
                Plugin.Log.LogWarning($"Could not switch disk logging on: {ex.Message}");
            }
        }

    }
}
