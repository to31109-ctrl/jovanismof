// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using MemoryPack;

namespace MegabonkTogether.Common.Messages
{
    /// <summary>
    /// Host to every client: checkpointed player state to restore, as the JSON of a
    /// <see cref="Common.Persistence.SavedPlayer"/> array. Each peer applies the slot whose
    /// connection id is its own to its real inventory and the remaining slots to the matching
    /// remote players, so every screen agrees. Sent when a run resumes from a checkpoint and
    /// when a player who left rejoins the same world.
    /// </summary>
    [MemoryPackable]
    public partial class WorldRestore : IGameNetworkMessage
    {
        public string WorldId { get; set; } = "";
        public long Revision { get; set; }
        public double ElapsedSeconds { get; set; }
        /// <summary>True when this restores one returning player rather than the whole world.</summary>
        public bool Rejoin { get; set; }
        public string PlayersJson { get; set; } = "";
    }
}
