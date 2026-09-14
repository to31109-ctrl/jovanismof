using System;
using System.IO;

namespace MegabonkTogether.Services
{
    /// <summary>
    /// Tells the launcher's splash window what the updater is doing.
    ///
    /// The splash is a separate PowerShell process started before the game, so it cannot be
    /// called into. It polls this file instead. Nothing here may throw: a launcher that cannot
    /// be told what is happening is a cosmetic problem, and must never stop the game loading.
    /// </summary>
    internal static class UpdateStatus
    {
        internal const string FileName = ".jovanismof-update";

        internal static void Checking() => Write("stage=checking");

        internal static void Downloading(string version, int percent)
        {
            // Below zero means the server did not say how big the download is, so the splash
            // keeps its indeterminate animation rather than showing a made-up number.
            var line = percent >= 0 ? $"percent={percent}" : "percent=-1";
            Write($"stage=downloading\nversion={version}\n{line}");
        }

        internal static void Ready(string version) => Write($"stage=ready\nversion={version}");

        internal static void UpToDate() => Write("stage=none");

        internal static void Failed(string message) => Write($"stage=failed\nmessage={Sanitize(message)}");

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            return value.Length > 200 ? value.Substring(0, 200) : value;
        }

        private static void Write(string content)
        {
            try
            {
                var path = Path.Combine(BepInEx.Paths.BepInExRootPath, FileName);
                File.WriteAllText(path, content);
            }
            catch
            {
                // The splash simply keeps showing whatever it last read.
            }
        }
    }
}
