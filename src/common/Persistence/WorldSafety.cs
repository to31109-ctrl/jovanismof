// BonkLink edition, 2026-09-15. GPL-2.0; see LICENSE.
using System.Collections.Generic;
using System.Linq;

namespace MegabonkTogether.Common.Persistence;

/// <summary>
/// The two rules that decide whether a saved world survives contact with a session, kept here as
/// plain functions so they can be tested without a game running.
///
/// Both were written after a world quietly stopped being worth loading, and neither was provable
/// while it lived inside the service.
/// </summary>
public static class WorldSafety
{
    /// <summary>
    /// Names a player whose capture came out empty when it should not have, or null when the
    /// checkpoint is safe to write.
    ///
    /// Nobody loses every weapon, item and upgrade at the same instant. A capture that says they
    /// have was taken while an inventory was not there to read -- a stage boundary being the
    /// obvious moment, where the local player's inventory is rebuilt while remote mirrors keep
    /// theirs. One session wrote `4 players, 0 enemies, 0s; incomplete: inventory for Burger,
    /// inventory for ******, inventory for Nigber` straight over a good world.
    /// </summary>
    public static string WhoWasCapturedEmpty(IEnumerable<SavedPlayer> now, IEnumerable<SavedPlayer> before)
    {
        if (now == null || before == null) return null;

        var previous = before.Where(p => p != null).ToList();

        foreach (var player in now)
        {
            if (player == null || Carried(player) > 0) continue;

            // Both halves insist on a real value. Two players who have no identity and no id yet
            // both hold the type's default, and matching on that made a newcomer with nothing
            // look like an established player who had just lost everything -- which would have
            // refused every checkpoint from the moment somebody joined.
            var was = previous.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(player.Identity) && p.Identity == player.Identity)
                || (player.PlayerId != System.Guid.Empty && p.PlayerId == player.PlayerId));

            if (was != null && Carried(was) > 0) return player.Name;
        }

        return null;
    }

    /// <summary>
    /// Whether a player reporting themselves ready should be handed their saved run back.
    ///
    /// The subtlety that cost a day: a checkpoint records who was connected **when it was
    /// written**, and a world is saved while everyone is playing -- so every slot in a saved
    /// world says connected. Read literally at load time that means "they are already here,
    /// leave them alone", and nobody was restored at all. Everyone in a world that has just been
    /// loaded is a returning player, whatever the file says.
    ///
    /// Mid-run the flag means what it says: clients report ready at the start of every stage, and
    /// handing them an older checkpoint each time would rewind them.
    /// </summary>
    public static bool ShouldRestore(bool connectedInSave, bool worldWasJustLoaded, bool alreadyRestoredThisSession)
    {
        if (alreadyRestoredThisSession) return false;
        if (worldWasJustLoaded) return true;
        return !connectedInSave;
    }

    private static int Carried(SavedPlayer player) =>
        (player.Upgrades?.Count ?? 0) + (player.Items?.Count ?? 0) + (player.Stats?.Count ?? 0);
}
