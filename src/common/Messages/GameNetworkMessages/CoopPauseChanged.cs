using MemoryPack;

namespace MegabonkTogether.Common.Messages.GameNetworkMessages
{
    /// <summary>
    /// The host holding the whole session, or letting it go again.
    ///
    /// Separate from the game's own pause, which stops only the player who opened it and which
    /// the mod otherwise refuses outright during a session. This is for waiting on somebody who
    /// dropped out and is coming back.
    /// </summary>
    [MemoryPackable]
    public partial class CoopPauseChanged : IGameNetworkMessage
    {
        public bool Paused { get; set; }
    }
}
