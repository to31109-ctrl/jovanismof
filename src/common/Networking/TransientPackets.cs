// BonkLink edition, 2026-09-16. GPL-2.0; see LICENSE.
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Common.Models;
using MemoryPack;

namespace MegabonkTogether.Common.Networking;

/// <summary>
/// Splits the two remaining per-tick state streams into datagram-sized pieces, so that neither
/// ever has to fall back to reliable, ordered delivery.
///
/// Player positions and projectile positions are transient: the next tick replaces them. Sent
/// reliably and in order, a lost packet holds up every packet behind it until the retransmit
/// arrives, and a machine that falls behind is made to work through the whole backlog in
/// sequence -- it can never skip to the present. That is what "they are in the past" is.
///
/// Both streams used to be sent as one message and switched to ReliableOrdered whenever it grew
/// past a datagram, which with more than a couple of players was always. The enemy stream had
/// already been given this treatment (EnemyPackets); these two had not.
/// </summary>
public static class TransientPackets
{
    /// <summary>What one unreliable datagram may carry, leaving room for the relay envelope.</summary>
    public const int MaxDatagramPayload = 1000;

    /// <summary>A projectile is about twenty bytes on the wire; thirty-two fits with room to spare.</summary>
    public const int ProjectilesPerPacket = 32;

    /// <summary>One player per packet. A player carries their whole inventory, so two might not fit.</summary>
    public static IEnumerable<byte[]> EncodePlayers(IEnumerable<Player> players)
    {
        foreach (var player in players)
        {
            if (player == null) continue;
            yield return MemoryPackSerializer.Serialize<IGameNetworkMessage>(new LobbyUpdates { Players = new[] { player } });
        }
    }

    public static IEnumerable<byte[]> EncodeProjectiles(IEnumerable<Projectile> projectiles)
    {
        foreach (var batch in projectiles.Chunk(ProjectilesPerPacket))
            yield return MemoryPackSerializer.Serialize<IGameNetworkMessage>(new ProjectilesUpdate { Projectiles = batch });
    }

    /// <summary>Desert tumbleweeds: the same rule, they are positions that the next tick replaces.</summary>
    public static IEnumerable<byte[]> EncodeTumbleWeeds(IEnumerable<TumbleWeedModel> tumbleWeeds)
    {
        foreach (var batch in tumbleWeeds.Chunk(ProjectilesPerPacket))
            yield return MemoryPackSerializer.Serialize<IGameNetworkMessage>(new TumbleWeedsUpdate { TumbleWeeds = batch });
    }
}
