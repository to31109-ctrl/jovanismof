// BonkLink edition changes, 2026-09-12: bounded snapshot scheduling and corrected match guard.
using MegabonkTogether.Common.Networking;
using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Managers;
using Il2CppInterop.Runtime;
using MegabonkTogether.Configuration;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MegabonkTogether.Scripts
{
    public class NetworkHandler : MonoBehaviour
    {
        private const float LOBBY_UPDATE_TICK_RATE = 30f;
        private const float lobbyUpdatetickInterval = 1f / LOBBY_UPDATE_TICK_RATE;
        private float lobbyUpdateAccumulator = 0f;

        private const float ENEMY_UPDATE_TICK_RATE = 20f;
        private const float enemyUpdatetickInterval = 1f / ENEMY_UPDATE_TICK_RATE;
        private float enemyUpdateAccumulator = 0f;

        private const float PROJECTILE_UPDATE_TICK_RATE = 20f;
        private const float projectileUpdatetickInterval = 1f / PROJECTILE_UPDATE_TICK_RATE;
        private float projectileUpdateAccumulator = 0f;

        private const float TUMBLEWEED_UPDATE_TICK_RATE = 20f;
        private const float tumbleWeedUpdatetickInterval = 1f / TUMBLEWEED_UPDATE_TICK_RATE;
        private float tumbleWeedUpdateAccumulator = 0f;

        private bool hasStarted = false;
        private bool? hasFoundMatch = null;
        private bool? hasJoinedFriendlyRoom = null;
        private bool? isConnectedToMatchMaker = null;
        private bool IsNetworkInterrupted = false;
        private string matchMakerFailureMessage = string.Empty;

        private bool isHost = false;
        private bool isGameStarted = false;

        private IUdpClientService udpClientService;
        private ISynchronizationService synchronizationService;
        private IWebsocketClientService websocketClientService;
        private IPlayerManagerService playerManagerService;
        private IWorldSaveService worldSaveService;

        public bool? IsConnectedToMatchMaker => isConnectedToMatchMaker;
        public string MatchMakerFailureMessage => matchMakerFailureMessage;
        public bool? HasFoundMatch => hasFoundMatch;

        public bool? HasJoinedFriendlyRoom => hasJoinedFriendlyRoom;
        public bool IsNetworkInterruptedStatus => IsNetworkInterrupted;

        public bool IsHost => isHost;

        public void Awake()
        {
            websocketClientService = Plugin.Services.GetRequiredService<IWebsocketClientService>();
            playerManagerService = Plugin.Services.GetRequiredService<IPlayerManagerService>();
            worldSaveService = Plugin.Services.GetRequiredService<IWorldSaveService>();

            EventManager.SubscribeGameStartedEvents(OnGameStarted);
            EventManager.SubscribePortalOpenedEvents(OnPortalOpened);
        }

        private void OnPortalOpened()
        {
            isGameStarted = false;
        }

        private void OnGameStarted()
        {
            isGameStarted = true;

            // Reaching a new area brings back anyone still down, so nobody spends the rest of
            // the run watching. Host only: it is the host that decides a player is alive again.
            try
            {
                if (Configuration.ModConfig.ReviveOnNewArea.Value && (synchronizationService.IsServerMode() ?? false))
                {
                    Scripts.Interactables.InteractableReviver.ReviveEveryoneWaiting();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Reviving players on a new area failed: {ex}");
            }
        }

        public void Update()
        {
            try
            {
                if (udpClientService == null || synchronizationService == null) return;

                if (hasFoundMatch == null) return;

                if (hasFoundMatch != true || synchronizationService.IsLoadingNextLevel()) return;

                udpClientService.Poll();

                if (GameManager.Instance == null || GameManager.Instance.player == null || GameManager.Instance.player.inventory == null) return;

                if (SnapshotSchedule.Due(ref lobbyUpdateAccumulator, Time.unscaledDeltaTime, lobbyUpdatetickInterval))
                    udpClientService.Update();
                if (isHost && isGameStarted)
                {
                    // BonkLink edition, 2026-09-13: periodic co-op world checkpoints.
                    worldSaveService?.Tick(Time.unscaledDeltaTime);
                    if (SnapshotSchedule.Due(ref enemyUpdateAccumulator, Time.deltaTime, enemyUpdatetickInterval)) udpClientService.UpdateEnemies();
                    if (SnapshotSchedule.Due(ref projectileUpdateAccumulator, Time.deltaTime, projectileUpdatetickInterval)) udpClientService.UpdateProjectiles();
                    if (MapController.runConfig.mapData.eMap == EMap.Desert && SnapshotSchedule.Due(ref tumbleWeedUpdateAccumulator, Time.deltaTime, tumbleWeedUpdatetickInterval)) udpClientService.UpdateTumbleWeeds();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"NetworkHandler Update error: {ex}");
            }
        }

        public int GetLobbySize()
        {
            return playerManagerService.GetAllPlayers().Count();
        }

        public void HandleNetworking()
        {
            try
            {
                _ = MainThreadDispatcher.Run(async () =>
                {
                    try
                    {
                        hasFoundMatch = null;
                        IsNetworkInterrupted = false;
                        matchMakerFailureMessage = string.Empty;
                        if (isConnectedToMatchMaker.HasValue && !isConnectedToMatchMaker.Value)
                        {
                            isConnectedToMatchMaker = null;
                        }

                        await websocketClientService.ConnectAndMatchAsync(ModConfig.ServerUrl.Value, ModConfig.RDVServerPort.Value, this);
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.LogError($"WebSocket connection error: {ex.Message}");
                        isConnectedToMatchMaker = false;
                        matchMakerFailureMessage = ex.Message;
                    }
                });

            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"WebSocket task error: {ex}");
            }
        }

        public void ResetNetworking()
        {
            isConnectedToMatchMaker = null;
            Plugin.Instance.Mode = new();
            isHost = false;

            try
            {
                udpClientService?.Reset();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Error resetting UDP client: {ex}");
            }
            finally
            {
                udpClientService = null;
            }

            try
            {
                synchronizationService?.Reset();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Error resetting Synchronization service: {ex}");
            }
            finally
            {
                synchronizationService = null;
            }

            playerManagerService?.Reset();

            if (websocketClientService != null)
            {
                try
                {
                    // Reset completes synchronously, so no stale teardown can close a new lobby.
                    _ = websocketClientService.Reset();
                }
                catch (System.Exception ex) { Plugin.Log.LogError($"Error resetting websocket: {ex}"); }
            }
        }

        public void OnConnectedToMatchMaker()
        {
            isConnectedToMatchMaker = true;
        }

        public void OnFailedToConnectToMatchMaker(string message)
        {
            isConnectedToMatchMaker = false;
            matchMakerFailureMessage = message;
        }

        public void OnNetworkInterrupted(string message)
        {
            IsNetworkInterrupted = true;
            matchMakerFailureMessage = message;
        }

        public void OnMatchFound(bool success)
        {
            hasFoundMatch = success;
            if (success)
            {
                udpClientService = Plugin.Services.GetRequiredService<IUdpClientService>();
                synchronizationService = Plugin.Services.GetRequiredService<ISynchronizationService>();
                if (Plugin.Instance.Mode.Mode == Common.Models.NetworkModeType.Random)
                {
                    isHost = synchronizationService.IsServerMode() ?? false;
                }
                else
                {
                    isHost = Plugin.Instance.Mode.Role == Common.Models.Role.Host;
                    udpClientService.UpdateMode(isHost);
                }
            }
            else
            {
                if (Plugin.Instance.Mode.Mode == Common.Models.NetworkModeType.Random)
                {
                    matchMakerFailureMessage = "Failed to establish P2P connection.";
                }
                IsNetworkInterrupted = true;
            }
        }

    }
}
