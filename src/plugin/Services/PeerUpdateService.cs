// BonkLink edition, 2026-09-13. GPL-2.0; see LICENSE.
using BepInEx.Logging;
using LiteNetLib;
using MegabonkTogether.Common;
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Configuration;
using MegabonkTogether.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace MegabonkTogether.Services
{
    /// <summary>
    /// Hands the host's build straight to a client that is running an older one, over the
    /// connection the two already have. It exists so a group can stay on the same version
    /// without anyone hosting files or sending archives by hand.
    ///
    /// This transfers program files between machines, so it is deliberately narrow: only a
    /// host may offer, only in a private Friendlies room, only a strictly newer version, and
    /// the archive must match the digest that was announced. It is applied on quit by the
    /// same mechanism a normal download uses. Either side can switch it off in configuration.
    /// </summary>
    public interface IPeerUpdateService
    {
        /// <summary>Host: a peer introduced itself; offer our build if theirs is older.</summary>
        void OnPeerIntroduced(NetPeer peer, uint connectionId, string theirVersion);

        /// <summary>Client: the host is offering a build.</summary>
        void OnUpdateOffered(ModUpdateOffer offer);

        /// <summary>Client: one slice of the offered build.</summary>
        void OnUpdateChunk(ModUpdateChunk chunk);

        void Reset();
    }

    internal sealed class PeerUpdateService : IPeerUpdateService
    {
        // Kept under a single datagram. Larger reliable payloads rely on fragmentation, which
        // this transport does not deliver here: 16 KiB pieces left the host and never arrived.
        private const int ChunkBytes = 1024;
        private const int MaximumArchiveBytes = 32 * 1024 * 1024;

        private readonly ManualLogSource logger;

        private byte[] ownArchive;
        private string ownArchiveDigest = "";
        private string ownArchiveName = "";

        private ModUpdateOffer incoming;
        private byte[][] incomingChunks;
        private int incomingReceived;

        public PeerUpdateService(ManualLogSource logger)
        {
            this.logger = logger;
            EventManager.SubscribeModUpdateOfferEvents(OnUpdateOffered);
            EventManager.SubscribeModUpdateChunkEvents(OnUpdateChunk);
        }

        private static string OwnVersion => MyPluginInfo.PLUGIN_VERSION;

        /// <summary>Only private rooms. A random public lobby is not somewhere to accept programs from.</summary>
        private static bool InPrivateRoom => Plugin.Instance?.Mode?.Mode == NetworkModeType.Friendlies;

        public void Reset()
        {
            incoming = null;
            incomingChunks = null;
            incomingReceived = 0;
        }

        // ---------------------------------------------------------------- host

        public void OnPeerIntroduced(NetPeer peer, uint connectionId, string theirVersion)
        {
            try
            {
                if (!ModConfig.ShareUpdatesWithPeers.Value) return;
                if (!InPrivateRoom) return;
                if (peer == null) return;
                if (string.IsNullOrEmpty(theirVersion)) return;  // an older build that cannot say
                if (!IsNewer(OwnVersion, theirVersion)) return;

                var archive = BuildOwnArchive();
                if (archive == null || archive.Length == 0) return;

                var totalChunks = (archive.Length + ChunkBytes - 1) / ChunkBytes;

                logger.LogInfo($"Offering build {OwnVersion} to player {connectionId} on {theirVersion} ({archive.Length / 1024} KiB, {totalChunks} pieces).");

                var udp = Plugin.Services.GetService<IUdpClientService>();
                if (udp == null) return;

                IGameNetworkMessage offer = new ModUpdateOffer
                {
                    Version = OwnVersion,
                    FileName = ownArchiveName,
                    TotalBytes = archive.Length,
                    TotalChunks = totalChunks,
                    Digest = ownArchiveDigest,
                };

                udp.SendToClient(peer, offer, connectionId);
                CoroutineRunner.Instance.Run(SendChunks(udp, peer, connectionId, archive, totalChunks));
            }
            catch (Exception ex)
            {
                logger.LogError($"Offering the build to a peer failed: {ex}");
            }
        }

        /// <summary>Spread over frames so a multi-megabyte send never stalls the lobby.</summary>
        private IEnumerator SendChunks(IUdpClientService udp, NetPeer peer, uint connectionId, byte[] archive, int totalChunks)
        {
            for (var index = 0; index < totalChunks; index++)
            {
                var offset = index * ChunkBytes;
                var length = Math.Min(ChunkBytes, archive.Length - offset);
                var slice = new byte[length];
                Buffer.BlockCopy(archive, offset, slice, 0, length);

                IGameNetworkMessage chunk = new ModUpdateChunk
                {
                    Version = OwnVersion,
                    Index = index,
                    Data = slice,
                };

                var failed = false;
                try { udp.SendToClient(peer, chunk, connectionId); }
                catch (Exception ex)
                {
                    failed = true;
                    logger.LogWarning($"Sending build piece {index + 1}/{totalChunks} failed: {ex.Message}");
                }

                if (failed) yield break;

                // A few per frame keeps the reliable channel from being swamped.
                if (index % 24 == 23) yield return null;
            }

            logger.LogInfo($"Finished sending build {OwnVersion} to player {connectionId}.");
        }

        /// <summary>Zips this installation's own plugin files, once, and remembers the result.</summary>
        private byte[] BuildOwnArchive()
        {
            if (ownArchive != null) return ownArchive;

            var updater = Plugin.Services.GetService<IAutoUpdaterService>();
            var directory = updater?.GetPluginDirectory();

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                logger.LogWarning("Cannot share a build: the plugin directory is unknown.");
                return null;
            }

            var files = Directory.GetFiles(directory)
                .Where(f => new[] { ".dll", ".json", ".toml" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal))
                .ToArray();

            if (files.Length == 0)
            {
                logger.LogWarning("Cannot share a build: no plugin files found.");
                return null;
            }

            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    // Flat entries: the applier extracts by name straight into the plugin folder.
                    var entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal);
                    using var source = File.OpenRead(file);
                    using var target = entry.Open();
                    source.CopyTo(target);
                }
            }

            var bytes = buffer.ToArray();
            if (bytes.Length > MaximumArchiveBytes)
            {
                logger.LogWarning($"Cannot share a build: {bytes.Length / (1024 * 1024)} MiB is too large to send.");
                return null;
            }

            ownArchive = bytes;
            ownArchiveDigest = Convert.ToHexString(SHA256.HashData(bytes));
            ownArchiveName = $"JOVANISMOF-{OwnVersion}.zip";
            return ownArchive;
        }

        // ---------------------------------------------------------------- client

        public void OnUpdateOffered(ModUpdateOffer offer)
        {
            Reset();

            if (offer == null) return;

            if (!ModConfig.AcceptUpdatesFromHost.Value)
            {
                logger.LogInfo("The host offered a newer build; accepting updates from the host is switched off.");
                return;
            }

            if (!InPrivateRoom)
            {
                logger.LogWarning("Refusing a build offered outside a private room.");
                return;
            }

            if (!IsNewer(offer.Version, OwnVersion))
            {
                logger.LogInfo($"Ignoring an offered build {offer.Version}; {OwnVersion} is not older.");
                return;
            }

            if (offer.TotalBytes <= 0 || offer.TotalBytes > MaximumArchiveBytes
                || offer.TotalChunks <= 0 || offer.TotalChunks > 65536
                || string.IsNullOrEmpty(offer.Digest))
            {
                logger.LogWarning("Refusing an offered build: the offer does not describe a sane archive.");
                return;
            }

            incoming = offer;
            incomingChunks = new byte[offer.TotalChunks][];
            incomingReceived = 0;

            logger.LogInfo($"The host is on build {offer.Version}; receiving it ({offer.TotalBytes / 1024} KiB).");
        }

        public void OnUpdateChunk(ModUpdateChunk chunk)
        {
            if (incoming == null || chunk == null) return;
            if (!string.Equals(chunk.Version, incoming.Version, StringComparison.Ordinal)) return;
            if (chunk.Index < 0 || chunk.Index >= incomingChunks.Length) return;
            if (incomingChunks[chunk.Index] != null) return;

            incomingChunks[chunk.Index] = chunk.Data ?? Array.Empty<byte>();
            incomingReceived++;

            if (incomingReceived < incomingChunks.Length) return;

            try
            {
                var total = incomingChunks.Sum(c => c.Length);
                if (total != incoming.TotalBytes)
                {
                    logger.LogWarning($"Refusing the received build: expected {incoming.TotalBytes} bytes, assembled {total}.");
                    Reset();
                    return;
                }

                var archive = new byte[total];
                var offset = 0;
                foreach (var piece in incomingChunks)
                {
                    Buffer.BlockCopy(piece, 0, archive, offset, piece.Length);
                    offset += piece.Length;
                }

                var digest = Convert.ToHexString(SHA256.HashData(archive));
                if (!string.Equals(digest, incoming.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Refusing the received build: it does not match the digest that was announced.");
                    Reset();
                    return;
                }

                var updater = Plugin.Services.GetService<IAutoUpdaterService>();
                if (updater != null && updater.StageUpdateArchive(archive, incoming.Version, incoming.FileName))
                {
                    Plugin.StartNotification(
                        ("MegabonkTogether", "UpdateAvailable"),
                        ("MegabonkTogether", "UpdateAvailable_Description"),
                        [incoming.Version],
                        null);

                    logger.LogInfo($"Build {incoming.Version} received from the host. It is applied when you close the game.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Applying the build received from the host failed: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        private static bool IsNewer(string candidate, string current)
        {
            try
            {
                return new Version((candidate ?? "").TrimStart('v')) > new Version((current ?? "").TrimStart('v'));
            }
            catch
            {
                return false;
            }
        }
    }
}
