using Assets.Scripts.Utility;
using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using TMPro;
using UnityEngine;
using Utility;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(SpawnPlayerPortal))]
    internal static class SpawnPlayerPortalPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
        private static TextMeshProUGUI synchronizeText;
        private static float dotAnimTimer = 0f;
        private static int dotCount = 0;
        public static Coroutine WaitForLobbyCoroutine;

        /// <summary>
        /// Wait for all players to be ready before starting the game
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnPlayerPortal.StartPortal))]
        public static void StartPortal_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            if (!synchronizationService.IsLobbyReady())
            {
                MyTime.Pause();

                if (WaitForLobbyCoroutine == null)
                {
                    WaitForLobbyCoroutine = CoroutineRunner.Instance.Run(WaitForLobbyReady());
                }
            }

        }

        /// <summary>When the wait stops looking normal and the player is told so.</summary>
        private const float SlowLobbySeconds = 15f;

        /// <summary>When waiting is abandoned and the run starts regardless.</summary>
        private const float GiveUpWaitingSeconds = 45f;

        private static IEnumerator WaitForLobbyReady()
        {
            Plugin.Log.LogInfo("Waiting for lobby to be ready");

            if (synchronizeText == null)
            {
                synchronizeText = new GameObject("synchronizeText").AddComponent<TMPro.TextMeshProUGUI>();
            }

            synchronizeText.enabled = true;
            synchronizeText.transform.SetParent(UiManager.Instance.transform);
            synchronizeText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            synchronizeText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            synchronizeText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            synchronizeText.rectTransform.anchoredPosition = new Vector2(0, 0);
            synchronizeText.alignment = TMPro.TextAlignmentOptions.Center;
            synchronizeText.text = "Waiting for other players";
            synchronizeText.fontSize = 48;

            dotAnimTimer = 0f;
            dotCount = 0;

            synchronizationService.TransitionToState(GameEvent.Ready);

            // This used to wait for ever. If one player's ready never arrives, everybody else
            // sits on "Waiting for other players" with no way out but closing the game.
            var waited = 0f;
            var sinceLastLog = 0f;
            var gaveUpWaiting = false;

            while (!synchronizationService.IsLobbyReady())
            {
                var step = Time.unscaledDeltaTime;
                waited += step;
                dotAnimTimer += step;
                sinceLastLog += step;

                if (dotAnimTimer >= 1f)
                {
                    dotAnimTimer = 0f;
                    dotCount = (dotCount + 1) % 4;
                    string dots = new string('.', dotCount);
                    synchronizeText.text = waited < SlowLobbySeconds
                        ? $"Waiting for other players {dots}"
                        : $"Waiting for other players {dots}\nTaking longer than usual. Starting shortly either way.";
                }

                // Logged occasionally rather than six times a second: the old line wrote to disk
                // continuously for as long as the wait lasted.
                if (sinceLastLog >= 5f)
                {
                    sinceLastLog = 0f;
                    Plugin.Log.LogInfo($"Lobby not ready after {waited:F0}s, still waiting...");
                }

                if (waited >= GiveUpWaitingSeconds)
                {
                    gaveUpWaiting = true;
                    break;
                }

                yield return new WaitForSeconds(0.17f);
            }

            if (gaveUpWaiting)
            {
                // Starting anyway beats leaving someone stuck on a screen they cannot dismiss.
                // Whoever is missing is still sent the run state when they do arrive.
                Plugin.Log.LogWarning($"Lobby never reported ready after {GiveUpWaitingSeconds:F0}s; starting anyway.");
            }
            else
            {
                Plugin.Log.LogInfo("Lobby is ready, starting the game");
            }

            synchronizationService.TransitionToState(GameEvent.Start);
            var seed = playerManagerService.GetSeed();
            MyRandom.random = new Il2CppSystem.Random(seed);

            synchronizeText.enabled = false;
            WaitForLobbyCoroutine = null;

            MyTime.Unpause();
        }
    }
}
