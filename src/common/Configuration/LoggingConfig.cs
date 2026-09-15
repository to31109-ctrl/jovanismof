// BonkLink edition. GPL-2.0; see LICENSE.
using System.Text.RegularExpressions;

namespace MegabonkTogether.Common;

/// <summary>
/// Makes sure a player actually gets a log file.
///
/// The installer switches BepInEx's console off so nobody plays behind a wall of scrolling
/// text, and on an installation that already carried a BepInEx config it left disk logging at
/// whatever BepInEx defaulted to -- which is off. Players then had no LogOutput.log at all, so
/// nothing they reported could be looked into from their own machine.
///
/// Repairing it in the installer reaches nobody who already has the mod, because an update
/// replaces the plugin and never runs the installer. The plugin therefore does it.
///
/// Kept here, away from the game, so it can be tested without one.
/// </summary>
public static class LoggingConfig
{
    /// <summary>Returns the config text with disk logging switched on, leaving the rest alone.</summary>
    public static string WithDiskLoggingOn(string text)
    {
        if (text == null) return null;

        var section = Regex.Match(text, @"(?ms)^\[Logging\.Disk\].*?(?=^\[|\z)");
        if (!section.Success)
        {
            return text.TrimEnd() + "\r\n\r\n[Logging.Disk]\r\nEnabled = true\r\n";
        }

        var body = section.Value;
        // Only the Enabled inside this section: the match above already stops at the next
        // heading, so the console's own setting is never touched.
        body = Regex.IsMatch(body, @"(?m)^Enabled\s*=")
            ? Regex.Replace(body, @"(?m)^Enabled\s*=.*$", "Enabled = true")
            : body.TrimEnd() + "\r\nEnabled = true\r\n";

        return text.Substring(0, section.Index) + body + text.Substring(section.Index + section.Length);
    }
}
