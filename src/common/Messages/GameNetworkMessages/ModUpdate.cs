// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using MemoryPack;

namespace MegabonkTogether.Common.Messages
{
    /// <summary>
    /// Host to client: "I am running a newer build than you, and here it comes."
    /// The archive follows as <see cref="ModUpdateChunk"/> messages. The client checks the
    /// version is genuinely newer and the digest matches before it stages anything.
    /// </summary>
    [MemoryPackable]
    public partial class ModUpdateOffer : IGameNetworkMessage
    {
        public string Version { get; set; } = "";
        public string FileName { get; set; } = "";
        public int TotalBytes { get; set; }
        public int TotalChunks { get; set; }
        /// <summary>Hex SHA-256 of the whole archive, checked once every chunk has arrived.</summary>
        public string Digest { get; set; } = "";
    }

    /// <summary>One slice of the archive announced by a <see cref="ModUpdateOffer"/>.</summary>
    [MemoryPackable]
    public partial class ModUpdateChunk : IGameNetworkMessage
    {
        public string Version { get; set; } = "";
        public int Index { get; set; }
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }
}
