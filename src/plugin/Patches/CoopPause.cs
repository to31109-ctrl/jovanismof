using Assets.Scripts.Utility;
using LiteNetLib;
using MegabonkTogether.Common.Messages.GameNetworkMessages;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// The host holding the whole session, so the party can wait for somebody who dropped out
    /// to come back rather than carrying on without them.
    ///
    /// Kept separate from the game's own pause: that one only stops the player who opened it,
    /// and in a netplay session the mod normally refuses it outright.
    /// </summary>
    internal static class CoopPause
    {
        /// <summary>True while the host is holding the session.</summary>
        internal static bool Held { get; private set; }

        internal static void Toggle() => SetAndTell(!Held);

        /// <summary>A client doing what the host said.</summary>
        internal static void ApplyFromHost(bool paused)
        {
            Held = paused;
            Apply(paused);
        }

        private static void SetAndTell(bool paused)
        {
            Held = paused;
            Apply(paused);

            try
            {
                Plugin.Services.GetService<IUdpClientService>()?.SendToAllClients(
                    new CoopPauseChanged { Paused = paused }, DeliveryMethod.ReliableOrdered);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not tell the other players about the pause: {ex.Message}");
            }
        }

        private static void Apply(bool paused)
        {
            try
            {
                if (paused)
                {
                    MyTime.Pause();
                    ScreenTextHelper.Show("<size=34>The host has paused the game</size>\nWaiting for a player to rejoin", new Vector2(0, -300), false);
                    Plugin.Log.LogInfo("Co-op session held by the host.");
                }
                else
                {
                    MyTime.Unpause();
                    ScreenTextHelper.Show("", new Vector2(0, -300), true);
                    Plugin.Log.LogInfo("Co-op session resumed by the host.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not apply the co-op pause: {ex.Message}");
            }
        }

        /// <summary>Nothing may stay frozen across a run; a stuck pause would be unrecoverable.</summary>
        internal static void Clear()
        {
            if (!Held) return;
            Held = false;
            try { MyTime.Unpause(); } catch { }
        }
    }
}
