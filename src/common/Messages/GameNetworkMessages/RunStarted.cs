using MemoryPack;

namespace MegabonkTogether.Common.Messages
{
    [MemoryPackable]
    public partial class RunStarted : IGameNetworkMessage
    {
        public int MapData { get; set; }
        public string StageData { get; set; }
        public int MapTierIndex { get; set; }
        public int MusicTrackIndex { get; set; }
        public string ChallengeName { get; set; }
        /// <summary>
        /// BonkLink edition: the seed this run generates from. Matchmaking hands out a fresh one
        /// per lobby, so a resumed world has to carry its own or the stage would come back different.
        /// </summary>
        public int Seed { get; set; }
        public Models.LobbyScaling Scaling { get; set; } = new();
    }
}
