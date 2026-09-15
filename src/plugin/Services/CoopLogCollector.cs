using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MegabonkTogether.Services
{
    /// <summary>
    /// Gathers the tail of every player's mod log on the host's machine.
    ///
    /// Nearly every fault in this mod looks different from each side, and telling which side is
    /// wrong has meant asking somebody to find a file and send it over. This collects them into
    /// one folder instead. Only this mod's own log is ever read, only the end of it, and only
    /// while a private room is being played.
    /// </summary>
    internal static class CoopLogCollector
    {
        /// <summary>Enough to cover a session's worth of faults without flooding the network.</summary>
        private const int MaximumTailBytes = 120_000;

        internal static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "BonkLinkLogs");

        /// <summary>The end of this player's own log, or empty when there is nothing to send.</summary>
        internal static string ReadOwnTail()
        {
            try
            {
                var path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
                if (!File.Exists(path)) return "";

                // Opened the way BepInEx holds it: it is still writing to this file.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length > MaximumTailBytes) stream.Seek(-MaximumTailBytes, SeekOrigin.End);

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not read my own log to send: {ex.Message}");
                return "";
            }
        }

        /// <summary>Writes one player's log where the host can find it.</summary>
        internal static void Store(string playerName, uint connectionId, string tail)
        {
            try
            {
                if (string.IsNullOrEmpty(tail)) return;

                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"{Sanitize(playerName)}-{connectionId}.log");
                File.WriteAllText(file, tail, new UTF8Encoding(false));
                Plugin.Log.LogInfo($"Saved {playerName}'s log to {file}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not save a player's log: {ex.Message}");
            }
        }

        /// <summary>A display name comes from another machine, so it is never trusted as a path.</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "player";
            var cleaned = Regex.Replace(name, @"[^A-Za-z0-9 _.-]", "");
            cleaned = cleaned.Trim().Replace(' ', '_');
            if (cleaned.Length > 40) cleaned = cleaned.Substring(0, 40);
            return string.IsNullOrEmpty(cleaned) ? "player" : cleaned;
        }
    }
}
