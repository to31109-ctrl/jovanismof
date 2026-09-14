using MegabonkTogether.Common.Messages;
using MemoryPack;

namespace MegabonkTogether.Common.Models
{
    [MemoryPackable]
    public partial class Player
    {
        public uint ConnectionId;
        public bool IsHost = false;
        public uint Character = 0;
        public string Skin = "";
        public bool IsReady = false;
        public string Name = "Player";
        // BonkLink edition, 2026-09-13: stable installation id used to match checkpointed players on rejoin.
        public string Identity = "";
        public QuantizedVector3 Position = new();
        public AnimatorState AnimatorState { get; set; } = new();
        public MovementState MovementState { get; set; } = new();

        public InventoryInfo Inventory { get; set; } = new();

        public uint Hp = 100;
        public uint MaxHp = 100;
        //public uint Xp = 0;
        public uint Shield = 0;
        public uint MaxShield = 0;
        // BonkLink edition, 2026-09-13: reported by the player themselves, for checkpoints.
        public int Gold = 0;
        public int Xp = 0;
        public int Level = 0;
        public float Overheal = 0;
        public int Banishes = 0;
        public int Refreshes = 0;
        public int Skips = 0;
        // BonkLink edition: every permanent stat upgrade this player holds. The host cannot read
        // these off a remote player's mirrored inventory, so the player reports them instead.
        public List<Persistence.SavedModifier> Stats { get; set; } = new();

    }
}
