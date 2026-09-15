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
        /// <summary>
        /// BonkLink edition: whether the host is running shared experience. Each peer used to
        /// answer this from whatever it happened to know locally, and a peer that did not know
        /// answered "no" -- so it never froze for a level-up choice and walked around while
        /// everybody else stood still.
        /// </summary>
        public bool SharedExperience { get; set; }
    }
}
