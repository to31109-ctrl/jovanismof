using MemoryPack;

namespace MegabonkTogether.Common.Messages
{
    [MemoryPackable]
    public partial class Introduced : IGameNetworkMessage
    {
        public uint ConnectionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsHost { get; set; }
        /// <summary>
        /// BonkLink edition: the mod version this peer is running, so a host on a newer build can
        /// hand it straight to them instead of everyone swapping files by hand.
        /// </summary>
        public string ModVersion { get; set; } = "";
    }
}
