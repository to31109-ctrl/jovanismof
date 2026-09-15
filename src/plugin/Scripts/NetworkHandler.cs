// BonkLink edition changes, 2026-09-12: bounded snapshot scheduling and corrected match guard.
using MegabonkTogether.Common.Messages.GameNetworkMessages;
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

        /// <summary>
        /// Stops the space bar taking a level-up choice for you.
        ///
        /// Unity treats space and enter as "submit" on whichever UI element is currently
        /// selected, so a tap of space pressed the skip button and the choice was gone. Nothing
        /// is selected while a choice is up, so there is nothing for submit to press; the mouse
        /// is unaffected.
        /// </summary>
        private void KeepChoiceOffTheKeyboard()
        {
            try
            {
                if (!synchronizationService.HasNetplaySessionStarted()) return;

                var windows = UiManager.Instance?.encounterWindows;
                var choiceOnScreen = windows != null && windows.activeEncounterWindow != null
                    && windows.activeEncounterWindow.gameObject.activeInHierarchy;
                if (!choiceOnScreen) return;

                var events = UnityEngine.EventSystems.EventSystem.current;
                if (events != null && events.currentSelectedGameObject != null)
                {
                    events.SetSelectedGameObject(null);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not keep the choice off the keyboard: {ex.Message}");
            }
        }

        private float stuckForSeconds;

        /// <summary>
        /// Notices the one state that should be impossible -- the world running, no choice on
        /// screen, and the player still unable to move -- and puts it right.
        ///
        /// A player who had already chosen could be left exactly there: everything moving
        /// around them, nothing to click, and no way to act. Whatever leads to it, being
        /// stranded for the rest of the run is the worst outcome available, so this clears it
        /// rather than waiting to find every cause.
        /// </summary>
        private void RecoverFromAStuckChoice()
        {
            try
            {
                if (!synchronizationService.HasNetplaySessionStarted()) { stuckForSeconds = 0f; return; }

                var player = GameManager.Instance?.player;
                if (player?.playerInput == null) { stuckForSeconds = 0f; return; }

                var windows = UiManager.Instance?.encounterWindows;
                var choiceOnScreen = windows != null
                    && (windows.encounterInProgress
                        || (windows.activeEncounterWindow != null && windows.activeEncounterWindow.gameObject.activeInHierarchy));

                // A choice being up, or time being stopped, are both perfectly normal.
                if (choiceOnScreen || Time.timeScale == 0f || WindowManager.HasOpenWindow() || player.playerInput.CanInput())
                {
                    stuckForSeconds = 0f;
                    return;
                }

                stuckForSeconds += Time.unscaledDeltaTime;
                if (stuckForSeconds < 3f) return;
                stuckForSeconds = 0f;

                Plugin.Log.LogWarning("This player has been unable to act with nothing on screen and the world running; clearing the leftover choice state.");

                if (windows != null)
                {
                    windows.encounterInProgress = false;
                    if (windows.activeEncounterWindow != null) windows.activeEncounterWindow.gameObject.SetActive(false);
                }

                Assets.Scripts.Utility.MyTime.Unpause();
                MegabonkTogether.Helpers.ScreenTextHelper.Show("", new Vector2(0, -350));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not clear a stuck choice: {ex.Message}");
                stuckForSeconds = 0f;
            }
        }

        private float sinceStatsChecked;
        private int lastReportedStatSignature;

        /// <summary>
        /// Tells the host every permanent stat upgrade this player holds, whenever the set
        /// changes. The host only holds a display mirror of a remote inventory and cannot read
        /// these off it, so without this a checkpoint saved the host's upgrades and nobody
        /// else's: everyone else came back at the right level with none of its power.
        ///
        /// Polled rather than hooked so a shrine, a level-up or anything else that grants a
        /// stat is caught the same way, and sent only when something actually changed.
        /// </summary>
        private void ReportOwnStatsIfChanged()
        {
            sinceStatsChecked += Time.unscaledDeltaTime;
            if (sinceStatsChecked < 1f) return;
            sinceStatsChecked = 0f;

            try
            {
                var permanent = GameManager.Instance?.player?.inventory?.statInventory?.permanentChanges;
                if (permanent == null) return;

                var stats = new System.Collections.Generic.List<Common.Persistence.SavedModifier>();
                var signature = 17;
                foreach (var entry in permanent)
                {
                    if (entry.Value == null) continue;
                    foreach (var modifier in entry.Value)
                    {
                        if (modifier == null || !float.IsFinite(modifier.modification)) continue;
                        stats.Add(new Common.Persistence.SavedModifier
                        {
                            Stat = (int)modifier.stat,
                            Operation = (int)modifier.modifyType,
                            Value = modifier.modification,
                        });
                        signature = signature * 31 + (int)modifier.stat;
                        signature = signature * 31 + (int)modifier.modifyType;
                        signature = signature * 31 + modifier.modification.GetHashCode();
                    }
                }

                if (signature == lastReportedStatSignature) return;
                lastReportedStatSignature = signature;

                var mine = playerManagerService.GetLocalPlayer();
                if (mine == null) return;

                // The host reads its own inventory directly; only a client has to send.
                if (isHost)
                {
                    mine.Stats = stats;
                    return;
                }

                udpClientService.SendToHost(new PlayerStatsReported
                {
                    ConnectionId = mine.ConnectionId,
                    Stats = stats,
                }, LiteNetLib.DeliveryMethod.ReliableOrdered);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not report stat upgrades to the host: {ex.Message}");
            }
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

                // Each of these is run on its own. They used to be one block, and a single
                // failure in the first of them -- the lobby broadcast -- meant none of the rest
                // ran at all: remote players stopped moving, no enemies or projectiles reached
                // anybody, checkpoints stopped being written and the level-up choice handling
                // went with them. A fault in one part of a frame must cost that part only.
                if (SnapshotSchedule.Due(ref lobbyUpdateAccumulator, Time.unscaledDeltaTime, lobbyUpdatetickInterval))
                    Step("sending the lobby update", udpClientService.Update);
                Step("reporting stat upgrades", ReportOwnStatsIfChanged);
                Step("recovering from a stuck choice", RecoverFromAStuckChoice);
                Step("keeping a choice off the keyboard", KeepChoiceOffTheKeyboard);
                Step("clearing a stale waiting notice", Patches.SpawnPlayerPortalPatches.ClearStaleWaitNotice);
                Step("slowing the world for a choice", Patches.ChoiceSlowMotion.Tick);

                if (isHost && isGameStarted)
                {
                    // BonkLink edition, 2026-09-13: periodic co-op world checkpoints.
                    Step("saving a checkpoint", () => worldSaveService?.Tick(Time.unscaledDeltaTime));
                    if (SnapshotSchedule.Due(ref enemyUpdateAccumulator, Time.deltaTime, enemyUpdatetickInterval))
                        Step("sending enemies", udpClientService.UpdateEnemies);
                    if (SnapshotSchedule.Due(ref projectileUpdateAccumulator, Time.deltaTime, projectileUpdatetickInterval))
                        Step("sending projectiles", udpClientService.UpdateProjectiles);
                    if (MapController.runConfig.mapData.eMap == EMap.Desert && SnapshotSchedule.Due(ref tumbleWeedUpdateAccumulator, Time.deltaTime, tumbleWeedUpdatetickInterval))
                        Step("sending tumbleweeds", udpClientService.UpdateTumbleWeeds);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"NetworkHandler Update error: {ex}");
            }
        }

        private readonly System.Collections.Generic.HashSet<string> reportedFaults = new();

        /// <summary>
        /// Runs one part of the frame, so that a fault in it cannot stop the parts after it.
        ///
        /// A repeat is counted rather than written out again: the fault this was built for threw
        /// on every frame and filled a fifteen megabyte log with thirteen thousand copies of the
        /// same stack, which buries everything else a player might need to report.
        /// </summary>
        private void Step(string what, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception ex)
            {
                var fault = $"{what}: {ex.GetType().Name}: {ex.Message}";
                if (reportedFaults.Add(fault))
                {
                    Plugin.Log.LogError($"Co-op kept going after a fault while {what}. This is a bug, please report it. " + ex);
                }
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
