using MemoryPack;

namespace MegabonkTogether.Common.Messages
{
    [MemoryPackable]
    public partial class ClientInGameReady : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
        /// <summary>BonkLink edition: stable installation id, so the host can return this player's checkpointed state.</summary>
        public string Identity { get; set; } = "";
    }
}
