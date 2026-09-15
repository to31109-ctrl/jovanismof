using MemoryPack;

namespace MegabonkTogether.Common.Messages.GameNetworkMessages
{
    /// <summary>The host asking everyone for the end of their log, so a fault can be read rather than guessed at.</summary>
    [MemoryPackable]
    public partial class LogRequested : IGameNetworkMessage
    {
    }

    /// <summary>
    /// A player handing the host the tail of their own mod log.
    ///
    /// Almost every desync this mod has had looked different from each side, and the only way
    /// to tell which side was wrong was to read both logs -- which meant asking someone to find
    /// a file and send it. This does that automatically, for the mod's own log and nothing else.
    /// </summary>
    [MemoryPackable]
    public partial class LogReported : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
        public string Name { get; set; } = "";
        /// <summary>The end of the log. The beginning is startup noise; faults are at the end.</summary>
        public string Tail { get; set; } = "";
    }
}
