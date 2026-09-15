// BonkLink edition, 2026-09-15. GPL-2.0; see LICENSE.
using System;

namespace MegabonkTogether.Common;

/// <summary>
/// Whether the host may hand its build straight to a player already in the room.
///
/// Offering a build sets "an update is waiting" on the player receiving it, and every build up
/// to and including 5.4.0 answers that by refusing the player's input: they can still walk,
/// because movement is an axis, but jump, interact and the pause menu all stop responding, so
/// they cannot even quit to apply the update. Handing an old client the host's build therefore
/// takes their controls away for the rest of the session. That block is gone from 5.4.1 on, and
/// anyone older is left alone -- their launcher updates them before the game starts next time,
/// which is the path that was always meant to carry them anyway.
/// </summary>
public static class PeerUpdateRules
{
    /// <summary>The first build that does not refuse the player's input while an update waits.</summary>
    public const string FirstBuildSafeToOffer = "5.4.1";

    /// <summary>
    /// True only when the host's build is strictly newer and the player can safely receive it.
    /// A player who does not say what they are running is treated as too old to be told.
    /// </summary>
    public static bool MayOffer(string ownVersion, string theirVersion)
    {
        var own = Parse(ownVersion);
        var theirs = Parse(theirVersion);
        var safe = Parse(FirstBuildSafeToOffer);

        if (own == null || theirs == null) return false;
        if (theirs < safe) return false;
        return own > theirs;
    }

    private static Version Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return new Version(value.Trim().TrimStart('v', 'V')); }
        catch { return null; }
    }
}
