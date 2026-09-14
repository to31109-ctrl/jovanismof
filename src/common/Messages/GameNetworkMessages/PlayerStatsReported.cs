using MegabonkTogether.Common.Persistence;
using MemoryPack;

namespace MegabonkTogether.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// A player telling the host every permanent stat upgrade they hold.
    ///
    /// The host only ever sees a display mirror of a remote player's inventory, and that mirror
    /// does not carry their stat upgrades. So checkpoints saved the host's own upgrades and
    /// nobody else's: everyone came back at the right level with none of the power it gave
    /// them. Sent only when the set actually changes, which is on a level-up or a shrine, so it
    /// costs nothing on a normal frame.
    /// </summary>
    [MemoryPackable]
    public partial class PlayerStatsReported : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
        public List<SavedModifier> Stats { get; set; } = new();
    }
}
