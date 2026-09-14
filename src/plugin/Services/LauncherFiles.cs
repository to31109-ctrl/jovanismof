using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace MegabonkTogether.Services
{
    /// <summary>
    /// Keeps the launcher's splash script up to date.
    ///
    /// An update only replaces files inside the plugin folder, because the batch that applies
    /// it flattens the archive into that one directory. The splash lives beside the game,
    /// outside the plugin folder, so it would otherwise stay on whatever version the player
    /// first installed and would never learn to show anything new. Carrying a copy inside the
    /// plugin and writing it out is what lets a change here reach people through an update.
    ///
    /// Only ever refreshes a file that is already there. A missing splash means this is not a
    /// launcher-based installation, and creating one would be inventing a launcher the player
    /// never asked for.
    /// </summary>
    internal static class LauncherFiles
    {
        private const string ResourceName = "JOVANISMOF.Splash.ps1";
        private const string FileName = "Splash.ps1";

        internal static void Refresh(BepInEx.Logging.ManualLogSource logger)
        {
            try
            {
                var gameRoot = Path.GetDirectoryName(BepInEx.Paths.BepInExRootPath);
                if (string.IsNullOrEmpty(gameRoot)) return;

                var target = Path.Combine(gameRoot, FileName);
                if (!File.Exists(target)) return;

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream == null) return;

                using var reader = new StreamReader(stream);
                var wanted = reader.ReadToEnd();

                // Compared by content so this rewrites once after an update and stays quiet
                // on every launch after that.
                if (string.Equals(File.ReadAllText(target), wanted, StringComparison.Ordinal)) return;

                // The launcher read its copy before the game started, so replacing it now is
                // safe; the new one is what runs next launch.
                File.WriteAllText(target, wanted, new UTF8Encoding(false));
                logger?.LogInfo("Updated the launcher splash screen; it takes effect next launch.");
            }
            catch (Exception ex)
            {
                // A stale splash is cosmetic. It must never stop the game loading.
                logger?.LogWarning($"Could not refresh the launcher splash screen: {ex.Message}");
            }
        }
    }
}
