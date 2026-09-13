// BonkLink edition addition, 2026-09-13. GPL-2.0; see LICENSE.
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Common.Models;
using MemoryPack;
namespace MegabonkTogether.Common.Networking;
public static class EnemyPackets
{
    // Keep transient snapshots small enough for a datagram, including relay envelope overhead.
    public static IEnumerable<byte[]> Encode(IEnumerable<EnemyModel> enemies, IEnumerable<BossOrbModel> orbs)
    {
        foreach (var batch in enemies.Chunk(16))
            yield return MemoryPackSerializer.Serialize<IGameNetworkMessage>(new LobbyUpdates { Enemies = batch });
        foreach (var batch in orbs.Chunk(16))
            yield return MemoryPackSerializer.Serialize<IGameNetworkMessage>(new LobbyUpdates { BossOrbs = batch });
    }
}
