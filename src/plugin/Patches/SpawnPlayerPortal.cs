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

            // Starting the run is a great deal of work and any of it can fail. It used to run
            // bare, so a fault there killed this coroutine on the spot and left the notice on
            // screen for the rest of the session -- while the run itself had already begun, so
            // the player was walking around behind "Waiting for other players" with no way to
            // dismiss it. The notice, the pause and the handle are cleared whatever happens.
            try
            {
                synchronizationService.TransitionToState(GameEvent.Start);
                var seed = playerManagerService.GetSeed();
                MyRandom.random = new Il2CppSystem.Random(seed);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Starting the run after waiting for the lobby failed: {ex}");
            }
            finally
            {
                HideWaitNotice();
                WaitForLobbyCoroutine = null;
                MyTime.Unpause();
            }
        }

        /// <summary>Takes the notice off the screen, whatever state it is in.</summary>
        private static void HideWaitNotice()
        {
            try
            {
                if (synchronizeText != null) synchronizeText.enabled = false;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not hide the waiting notice: {ex.Message}");
            }
        }

        /// <summary>
        /// Takes the notice down if it is still up while the player is playing.
        ///
        /// Whatever leaves it there, a player being told to wait for people who are already in
        /// the game with them -- with no way to dismiss it -- is never right, and it hides the
        /// middle of their screen for the rest of the run. Checked every frame rather than
        /// fixed at each cause, because it has now come back twice from different directions.
        /// </summary>
        internal static void ClearStaleWaitNotice()
        {
            if (synchronizeText == null || !synchronizeText.enabled) return;

            // A real wait holds the world still. If the world is running the player is playing,
            // and telling them to wait for people who are already in the game with them is
            // simply wrong -- whether the coroutine is still spinning or died on its way out.
            if (Time.timeScale == 0f) return;
            if (GameManager.Instance == null || GameManager.Instance.player == null) return;

            Plugin.Log.LogWarning("The 'waiting for other players' notice was still up while the world was running; taking it down.");
            HideWaitNotice();
            WaitForLobbyCoroutine = null;
        }
    }
}
