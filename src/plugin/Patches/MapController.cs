using Assets.Scripts.Game.Other;
using Assets.Scripts.Managers;
using HarmonyLib;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Extensions;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(MapController))]
    internal static class MapControllerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IUdpClientService udpClientService = Plugin.Services.GetService<IUdpClientService>();
        private static readonly IWebsocketClientService websocketClientService = Plugin.Services.GetService<IWebsocketClientService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        private static bool isWaitingForServerResponse = false;
        private static bool startApproved;

        /// <summary>
        /// Prevent Host from starting a new map until all players are ready (selected character)
        /// In friendlies, also notify server that game is starting to prevent new players from joining
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MapController.StartNewMap))]
        public static bool StartNewMap_Prefix(RunConfig newRunConfig)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            if (TransitionUI.Instance.isTransitioning)
            {
                return false;
            }

            var isHost = synchronizationService.IsServerMode() ?? false;

            if (!isHost)
            {
                UnityEngine.Random.InitState(playerManagerService.GetSeed());
                return true;
            }

            Plugin.Instance.IS_HOST_READY = true;

            // The stage change goes ahead whatever anyone's ready flag says, and every client
            // follows it. Refusing to move until all peers reported ready is what left a player
            // behind in the previous area -- still fighting a boss the rest of the party had
            // killed, unable to follow, while everyone else stood at the next portal unable to
            // continue. A portal takes the whole party, every time. It is the same rule as the
            // level-up choice: nothing in a session may depend on every player checking in.

            var isFriendlyMode = Plugin.Instance.Mode.Mode == NetworkModeType.Friendlies;

            // Telling the matchmaking server the game has begun is exactly what makes it turn
            // away anyone trying to join afterwards, which is why somebody who closed the game
            // could never get back into the run. A private room already needs its code to enter,
            // so leaving it open costs nothing and is what lets them return.
            if (isFriendlyMode && Configuration.ModConfig.KeepLobbyOpenForRejoin.Value)
            {
                Plugin.Log.LogInfo("Leaving the room open so a player who drops out can rejoin this run.");
            }
            else if (isFriendlyMode && !startApproved)
            {
                if (isWaitingForServerResponse) return false;
                isWaitingForServerResponse = true;
                CoroutineRunner.Instance.Run(NotifyServerAndStartGame(newRunConfig));
                return false;
            }

            Plugin.Instance.IS_HOST_READY = false;
            Plugin.Instance.HideModal();
            AnnounceRunStart(newRunConfig);
            return true;
        }

        private static IEnumerator NotifyServerAndStartGame(RunConfig runConfig)
        {
            Plugin.Instance.ShowModal("Locking lobby...");

            try
            {
                var task = websocketClientService.SendGameStarting();
                while (!task.IsCompleted) yield return null;
                if (task.IsCompletedSuccessfully && task.Result)
                {
                    startApproved = true;
                    MapController.StartNewMap(runConfig);
                }
                else
                {
                    Plugin.Log.LogError($"Failed to lock lobby: {task.Exception?.GetBaseException().Message ?? "timeout or rejection"}");
                    Plugin.Instance.HideModal();
                    Plugin.Instance.ShowModal("Failed to lock lobby. Please try again.");
                }
            }
            finally
            {
                startApproved = false;
                isWaitingForServerResponse = false;
                Plugin.Instance.IS_HOST_READY = false;
            }
        }

        /// <summary>
        /// Synchronize run start to clients if all players are ready
        /// </summary>
        // Set the shared seed and loading state before native stage generation begins.
        // Only the approved host call reaches this announcement.
        private static void AnnounceRunStart(RunConfig newRunConfig)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            var isHost = synchronizationService.IsServerMode() ?? false;

            if (!isHost)
            {
                return;
            }

            if (synchronizationService.IsLoading())
            {
                return;
            }

            UnityEngine.Random.InitState(playerManagerService.GetSeed());
            synchronizationService.OnRunStarted(newRunConfig);

            var allPlayers = playerManagerService.GetAllPlayers().ToList();
            var playerCount = allPlayers.Count;
            var mapName = newRunConfig.mapData.eMap.GetMapName();
            var stageName = newRunConfig.stageData.name;
            var stageIndex = newRunConfig.mapData.stages.IndexOf(newRunConfig.stageData);
            var characters = allPlayers.Select(p => ((ECharacter)p.Character).ToString()).ToList();

            _ = websocketClientService.SendRunStatistics(playerCount, mapName, stageIndex + 1, characters);
        }

        /// <summary>
        /// Prevent restarting run in netplay session
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MapController.RestartRun))]
        public static bool RestartRun_Prefix()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            return false;
        }

        //[HarmonyPrefix]
        //[HarmonyPatch(nameof(MapController.LoadNextStage))]
        //public static bool LoadNextStage_Prefix()
        //{
        //    if (MapController.index == 0)
        //    {
        //        MapController.LoadFinalStage();
        //        return false;
        //    }

        //    return true;
        //}

        //[HarmonyPostfix]
        //[HarmonyPatch(nameof(MapController.LoadNextStage))]
        //public static void LoadNextStage_Postfix()
        //{
        //    MapController.LoadFinalStage();
        //}
    }
}
