// BonkLink edition test harness, 2026-09-12. GPL-2.0; see LICENSE.
#if BONKLINK_TESTING
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Common.Persistence;
using MegabonkTogether.Persistence;
using MegabonkTogether.Helpers;
using MegabonkTogether.Scripts.Button;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Managers;
using Assets.Scripts.Actors;
using Assets.Scripts.Game.Combat;
using MegabonkTogether.Scripts.Interactables;
using Assets.Scripts.Utility;
using UnityEngine;
namespace MegabonkTogether;
[HarmonyPatch]
static class SmokeSavePaths
{
    static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(SaveManager), "GetDataPath"), AccessTools.Method(typeof(SaveManager), "GetDataPathDefault") };
    static bool Prefix(ref string __result)
    {
        __result = Path.Combine(BepInEx.Paths.BepInExRootPath, "TestSaves");
        Directory.CreateDirectory(__result);
        return false;
    }
}
[HarmonyPatch]
static class SmokeSaveWrites
{
    static IEnumerable<MethodBase> TargetMethods() => typeof(SaveManager).GetMethods().Where(m => m.Name is "SaveConfig" or "SaveStats" or "SaveProgression" or "SaveTemp" or "SaveControllers");
    static bool Prefix() => false;
}
public class BonkLinkSmoke : MonoBehaviour
{
    private float elapsed;
    private readonly System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
    private readonly DateTime launchTime = DateTime.UtcNow;
    private bool done;
    private bool started;
    private bool selected;
    private bool confirmed;
    private bool runStarted;
    private float lastReport;
    private float liveAt;
    private bool goldSent;
    private bool hitSent;
    private bool deathSent;
    private bool coffinUsed;
    private bool ghostKilled;
    private float coffinAt;
    private bool worldSaveChecked;
    private int walletProbeStep;

    private void DriveWalletProbe(bool host, float liveSeconds)
    {
        var inventory = GameManager.Instance.player.inventory;
        if (walletProbeStep < 5) Assets.Scripts.Utility.MyTime.Pause();
        if (walletProbeStep == 0)
        {
            inventory._gold_k__BackingField = 0;
            inventory._goldInt_k__BackingField = 0;
            walletProbeStep = 1;
        }
        if (walletProbeStep == 1 && liveSeconds > (host ? 8 : 14))
        {
            inventory.ChangeGold(host ? 37 : 19);
            walletProbeStep = 2;
        }
        if (walletProbeStep == 2 && liveSeconds > 22)
        {
            Plugin.Log.LogInfo($"BONKLINK_WALLET_SHARED: actual={inventory.goldInt} expected=56 pass={inventory.goldInt == 56}");
            inventory.ChangeGold(host ? -7 : -11);
            walletProbeStep = 3;
        }
        if (walletProbeStep == 3 && liveSeconds > 30)
        {
            var expected = host ? 49 : 45;
            Plugin.Log.LogInfo($"BONKLINK_WALLET_SPEND: actual={inventory.goldInt} expected={expected} pass={inventory.goldInt == expected}");
            var chest = Plugin.Services.GetRequiredService<ISpawnedObjectManagerService>().GetAllSpawnedObjects()
                .Select(o => o.obj.GetComponent<Assets.Scripts.Inventory__Items__Pickups.Chests.InteractableChest>()).FirstOrDefault(c => c != null && c.chestType == Assets.Scripts.Inventory__Items__Pickups.Interactables.EChest.Normal);
            if (chest != null)
            {
                var price = chest.GetPrice();
                inventory._gold_k__BackingField = price;
                inventory._goldInt_k__BackingField = price;
                Plugin.Log.LogInfo($"BONKLINK_CHEST_EXACT: price={price} affordable={chest.CanAfford()}");
                inventory._gold_k__BackingField = expected;
                inventory._goldInt_k__BackingField = expected;
            }
            else Plugin.Log.LogError("BONKLINK_CHEST_EXACT: no chest available for test");
            walletProbeStep = 4;
        }
        if (walletProbeStep == 4 && liveSeconds > (host ? 45 : 38))
        {
            inventory._gold_k__BackingField = 0;
            inventory._goldInt_k__BackingField = 0;
            if (host)
            {
                var chest = Plugin.Services.GetRequiredService<ISpawnedObjectManagerService>().GetAllSpawnedObjects()
                    .Select(o => o.obj.GetComponent<Assets.Scripts.Inventory__Items__Pickups.Chests.InteractableChest>()).FirstOrDefault(c => c != null && c.chestType == Assets.Scripts.Inventory__Items__Pickups.Interactables.EChest.Normal);
                if (chest != null)
                {
                    var price = chest.GetPrice();
                    inventory._gold_k__BackingField = price;
                    inventory._goldInt_k__BackingField = price;
                    Plugin.Services.GetRequiredService<ISynchronizationService>().OnInteractableUsed(chest);
                    var bought = chest.Interact();
                    Plugin.Log.LogInfo($"BONKLINK_CHEST_ENTER: type={chest.chestType} price={price} accepted={bought} remaining={inventory.goldInt}");
                }
            }
            walletProbeStep = 5;
        }
        if (walletProbeStep == 5 && liveSeconds > 60)
        {
            var window = UnityEngine.Object.FindObjectOfType<ChestWindowUi>();
            if (window == null) Plugin.Log.LogError("BONKLINK_CHEST_BUY: no chest window");
            else
            {
                MyTime.Unpause();
                window.OpenButton();
                Plugin.Log.LogInfo($"BONKLINK_CHEST_OPEN: remaining={inventory.goldInt}");
            }
            walletProbeStep = 6;
        }
        if (walletProbeStep == 6 && liveSeconds > 75)
        {
            Plugin.Log.LogInfo($"BONKLINK_CHEST_BUY: remaining={inventory.goldInt} pass={inventory.goldInt == 0}");
            walletProbeStep = 7;
        }
    }
    private bool menuUiChecked;
    private bool pauseUiChecked;
    private int marathonStage = -1;
    private float stageLiveAt;
    private bool stageBossSpawned;
    private uint stageBossId;
    private bool stageBossDead;
    private bool stagePortalUsed;
    private int stagesCleared;
    private float marathonActionAt;
    private float worstFrame;
    private bool bossSpawned;
    private bool bossCheckpointed;
    private uint bossId;
    private float bossDamageAt;
    private bool bossDead;
    private bool realGhostKilled;
    private float ghostHuntAt;
    public void Update()
    {
        Application.runInBackground = true;
        elapsed = (float)(DateTime.UtcNow - launchTime).TotalSeconds;
        if (elapsed > 180 && Environment.GetCommandLineArgs().Contains("--bonklink.walletcheck")) { Application.Quit(); return; }
        if (elapsed < 15 || done) return;
        var args = Environment.GetCommandLineArgs();
        bool host = args.Contains("--bonklink.host");
        bool client = args.Contains("--bonklink.client");
        int requiredPlayers = args.Contains("--bonklink.five") ? 5 : 2;
        // The boss test needs the host alive long enough to finish; an idle host standing next
        // to a stage boss dies in seconds and ends the session before the later phases run.
        if (host && (args.Contains("--bonklink.bosstest") || args.Contains("--bonklink.marathon")))
        {
            var hostHealth = GameManager.Instance?.player?.inventory?.playerHealth;
            if (hostHealth != null && hostHealth.hp < hostHealth.maxHp) hostHealth.hp = hostHealth.maxHp;
        }
        if (!menuUiChecked && elapsed > 16 && args.Contains("--bonklink.uicheck"))
        {
            menuUiChecked = true;
            CheckMenuUi();
        }
        if (host || client)
        {
            var codeFile = Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, "..", "tools", "test-room.txt"));
            if (!started && (host || (File.Exists(codeFile) && File.GetLastWriteTimeUtc(codeFile) > launchTime)))
            {
                var roomCode = "";
                if (!host)
                {
                    try { roomCode = File.ReadAllText(codeFile).Trim(); }
                    catch (IOException) { return; }
                    if (string.IsNullOrEmpty(roomCode)) return;
                }
                started = true;
                Plugin.Instance.Mode = new NetworkMode { Mode = NetworkModeType.Friendlies, Role = host ? Role.Host : Role.Client, RoomCode = roomCode, EnabledSharedExperience = true };
                Plugin.Instance.NetworkHandler.HandleNetworking();
            }
            if (!selected && Plugin.Instance.NetworkHandler.HasFoundMatch == true)
            {
                selected = true;
                if (host)
                {
                    File.WriteAllText(codeFile + ".tmp", Plugin.Instance.Mode.RoomCode);
                    File.Move(codeFile + ".tmp", codeFile, true);
                }
                Plugin.Instance.GetMainMenu().GoToCharacterSelection();
            }
            if (selected && !confirmed && Plugin.Instance.NetworkHandler.GetLobbySize() >= requiredPlayers && elapsed > 35)
            {
                confirmed = true;
                var menu = Plugin.Instance.GetMainMenu();
                CharacterMenu.selectedCharacter = (ECharacter)0;
                Plugin.Log.LogInfo("BONKLINK_CHARACTER_CONFIRM");
                menu.GoToMapSelection();
            }
            if (host && confirmed && !runStarted && elapsed > 50 && Plugin.Services.GetRequiredService<IUdpClientService>().AreAllPeersReady())
            {
                runStarted = true;
                Plugin.Log.LogInfo("BONKLINK_RUN_REQUESTED");
                Plugin.Instance.GetMainMenu().mapSelectionUi.StartMap();
            }
            if (elapsed - lastReport > 5)
            {
                lastReport = elapsed;
                var udp = Plugin.Services.GetRequiredService<IUdpClientService>();
                Plugin.Log.LogInfo($"BONKLINK_LOBBY: players={Plugin.Instance.NetworkHandler.GetLobbySize()} matched={Plugin.Instance.NetworkHandler.HasFoundMatch} ready={udp.AreAllPeersReady()} wall={elapsed:F1} liveAt={liveAt:F1} walletStep={walletProbeStep} started={Plugin.Services.GetRequiredService<ISynchronizationService>().HasNetplaySessionStarted()}");
                if (Plugin.Services.GetRequiredService<ISynchronizationService>().HasNetplaySessionStarted())
                {
                    if (liveAt == 0) liveAt = elapsed;
                    if (args.Contains("--bonklink.walletcheck"))
                    {
                        DriveWalletProbe(host, elapsed - liveAt);
                        if (elapsed > 140) Application.Quit();
                        return;
                    }
                    var manager = Plugin.Services.GetRequiredService<IEnemyManagerService>();
                    var enemies = ((IEnumerable<KeyValuePair<uint, Enemy>>)AccessTools.Field(manager.GetType(), "spawnedEnemies").GetValue(manager)).Where(p => p.Value != null).OrderBy(p => p.Key).ToArray();
                    Plugin.Log.LogInfo($"BONKLINK_WORLD: gold={GameManager.Instance.player.inventory.goldInt} enemies={enemies.Length} state=" + string.Join(";", enemies.Take(20).Select(p => $"{p.Key}:{p.Value.hp:F1}")));
                    if (!goldSent && elapsed - liveAt > (host ? 5 : 15))
                    {
                        goldSent = true;
                        var amount = host ? 37 : 19;
                        Plugin.Log.LogInfo($"BONKLINK_GOLD_ADD: {amount}");
                        GameManager.Instance.player.inventory.ChangeGold(amount);
                    }
                    if (!hitSent && enemies.Length > 0 && elapsed - liveAt > (host ? 20 : 30))
                    {
                        hitSent = true;
                        var target = enemies[0];
                        Plugin.Log.LogInfo($"BONKLINK_HIT: id={target.Key} before={target.Value.hp}");
                        target.Value.Damage(new DamageContainer(0, MegabonkTogether.Patches.Enemies.EnemyPatch.AllowedDamageSource.First()) { damage = 3 });
                    }
                    Plugin.Log.LogInfo($"BONKLINK_HEALTH: hp={GameManager.Instance.player.inventory.playerHealth.hp} visible={GameManager.Instance.player.playerRenderer.gameObject.activeSelf}");
                    if (host && !worldSaveChecked && elapsed - liveAt > 25)
                    {
                        worldSaveChecked = true;
                        CheckWorldSave();
                    }
                    if (args.Contains("--bonklink.bosstest"))
                    {
                        DriveBossTest(host, client, elapsed - liveAt);
                    }
                    if (args.Contains("--bonklink.marathon"))
                    {
                        DriveMarathon(host);
                    }
                    if (host && !pauseUiChecked && args.Contains("--bonklink.uicheck") && elapsed - liveAt > 15)
                    {
                        pauseUiChecked = true;
                        CheckPauseUi();
                    }
                    if (client && (requiredPlayers == 2 || args.Contains("--bonklink.revive")) && !deathSent && elapsed - liveAt > 40)
                    {
                        deathSent = true;
                        Plugin.Log.LogInfo("BONKLINK_FORCE_DEATH");
                        GameManager.Instance.player.inventory.playerHealth.hp = 0;
                        GameManager.Instance.player.inventory.playerHealth.PlayerDied();
                    }
                    if (host && !coffinUsed && elapsed - liveAt > 45)
                    {
                        var objects = Plugin.Services.GetRequiredService<ISpawnedObjectManagerService>().GetAllSpawnedObjects();
                        var coffin = objects.Select(p => p.obj.GetComponent<InteractableReviver>()).FirstOrDefault(c => c != null);
                        if (coffin != null)
                        {
                            coffinUsed = true;
                            coffinAt = elapsed;
                            MyTime.Unpause();
                            Plugin.Log.LogInfo("BONKLINK_COFFIN_INTERACT");
                            coffin.Interact();
                        }
                    }
                    if (host && coffinUsed && !ghostKilled && elapsed > coffinAt + 8 && !args.Contains("--bonklink.bosstest")) // the boss test kills the ghost for real instead
                    {
                        var ghost = enemies.FirstOrDefault(p => manager.GetReviverEnemy_Name(p.Value) != null);
                        if (ghost.Value != null)
                        {
                            ghostKilled = true;
                            Plugin.Log.LogInfo("BONKLINK_GHOST_DEATH_EVENT: injected to test the revive callback independently of paused combat");
                            ghost.Value.EnemyDied(new DamageContainer(0, "BonkLink test"));
                        }
                    }
                }
            }
            var duration = args.Contains("--bonklink.quick") ? 75
                : args.Contains("--bonklink.marathon") ? 1500
                : args.Contains("--bonklink.bosstest") ? 260 : 150;
            if (elapsed < duration) return;
        }
        done = true;
        var patches = Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m)?.Owners.Contains(MyPluginInfo.PLUGIN_GUID) == true);
        Plugin.Log.LogInfo($"BONKLINK_SMOKE_OK: Update reached; {patches} patched methods; network handler present={Plugin.Instance.NetworkHandler != null}");
        Application.Quit();
    }

    /// <summary>
    /// Plays a long session the way a party would: clear the stage boss, take the portal to the
    /// next stage, and keep going. Reports stage transitions, checkpoints and the worst frame
    /// seen, so a run that degrades over time shows up rather than merely "it did not crash".
    /// </summary>
    private void DriveMarathon(bool host)
    {
        if (Time.unscaledDeltaTime > worstFrame) worstFrame = Time.unscaledDeltaTime;

        int stage;
        try { stage = MapController.index; } catch { return; }

        if (stage != marathonStage)
        {
            if (marathonStage >= 0) stagesCleared++;
            marathonStage = stage;
            stageLiveAt = elapsed;
            stageBossSpawned = false;
            stageBossDead = false;
            stagePortalUsed = false;
            stageBossId = 0;
            Plugin.Log.LogInfo($"BONKLINK_MARATHON_STAGE: index={stage} name={SafeStageName()} cleared={stagesCleared} at={elapsed:F0}s");
        }

        var enemyManager = Plugin.Services.GetRequiredService<IEnemyManagerService>();
        var alive = enemyManager.GetAllSpawnedEnemies().Count(p => p.Value != null);
        Plugin.Log.LogInfo($"BONKLINK_MARATHON: t={elapsed:F0}s stage={marathonStage} enemies={alive} cleared={stagesCleared} worstFrame={worstFrame * 1000f:F0}ms mem={GC.GetTotalMemory(false) / (1024 * 1024)}MB");
        worstFrame = 0f;

        if (!host) return;

        var sinceStage = elapsed - stageLiveAt;

        if (!stageBossSpawned && sinceStage > 25)
        {
            stageBossSpawned = true;
            SpawnStageBoss();
            stageBossId = bossId;
            return;
        }

        if (stageBossSpawned && stageBossId != 0 && !stageBossDead && elapsed - marathonActionAt > 1f)
        {
            marathonActionAt = elapsed;
            var boss = enemyManager.GetEnemyById(stageBossId);

            if (boss == null || boss.IsDead())
            {
                stageBossDead = true;
                Plugin.Log.LogInfo($"BONKLINK_MARATHON_BOSS_DEAD: stage={marathonStage} id={stageBossId} at={elapsed:F0}s");
            }
            else
            {
                boss.Damage(new DamageContainer(0, MegabonkTogether.Patches.Enemies.EnemyPatch.AllowedDamageSource.First()) { damage = Math.Max(1f, boss.maxHp / 3f) });
            }
        }

        // Taking the portal is the real stage transition: it is what the whole party follows.
        if (stageBossDead && !stagePortalUsed && elapsed - marathonActionAt > 3f)
        {
            marathonActionAt = elapsed;
            try
            {
                var objectManager = Plugin.Services.GetRequiredService<ISpawnedObjectManagerService>();
                var spawner = objectManager.GetSpecific<InteractableBossSpawner>();
                var portalObject = spawner?.portal;

                if (portalObject == null)
                {
                    // The last stage of a map uses the final spawner instead of the ordinary
                    // boss portal, which is why earlier runs stopped here.
                    var finalSpawner = objectManager.GetSpecific<InteractableBossSpawnerFinal>();
                    if (finalSpawner != null)
                    {
                        stagePortalUsed = true;
                        MyTime.Unpause();
                        Plugin.Log.LogInfo($"BONKLINK_MARATHON_FINAL: stage {marathonStage} ends at the final boss spawner");
                        finalSpawner.Interact();
                        return;
                    }

                    Plugin.Log.LogWarning($"BONKLINK_MARATHON_PORTAL_FAIL: no portal or final spawner on stage {marathonStage}");
                    stagePortalUsed = true;
                    return;
                }

                portalObject.SetActive(true);
                var portal = portalObject.GetComponent<InteractablePortal>();

                if (portal == null)
                {
                    Plugin.Log.LogWarning($"BONKLINK_MARATHON_PORTAL_FAIL: portal has no interactable on stage {marathonStage}");
                    stagePortalUsed = true;
                    return;
                }

                stagePortalUsed = true;
                MyTime.Unpause();
                Plugin.Log.LogInfo($"BONKLINK_MARATHON_PORTAL: taking the portal from stage {marathonStage} at {elapsed:F0}s");
                portal.Interact();
            }
            catch (Exception ex)
            {
                stagePortalUsed = true;
                Plugin.Log.LogError($"BONKLINK_MARATHON_PORTAL_FAIL: {ex}");
            }
        }
    }

    /// <summary>
    /// Opens the mod's own menu the way a player does, so the world picker and the option
    /// toggles are actually built once rather than only compiled.
    /// </summary>
    private void CheckMenuUi()
    {
        try
        {
            var mainMenu = Plugin.Instance.GetMainMenu();
            if (mainMenu == null)
            {
                Plugin.Log.LogWarning("BONKLINK_UI_FAIL: no main menu to open the mod menu from");
                return;
            }

            var modalObject = new GameObject("NetworkMenuTab");
            var tab = modalObject.AddComponent<MegabonkTogether.Scripts.NetworkMenuTab>();
            Plugin.Instance.NetworkTab = tab;
            tab.SetMainMenu(mainMenu);

            // Give the modal a frame's worth of Unity lifecycle before inspecting it.
            CoroutineRunner.Instance.Run(InspectMenuUi(modalObject));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"BONKLINK_UI_FAIL: building the mod menu threw: {ex}");
        }
    }

    private System.Collections.IEnumerator InspectMenuUi(GameObject modalObject)
    {
        yield return null;
        yield return null;

        var missing = new List<string>();
        var present = new List<string>();

        var tab = Plugin.Instance.NetworkTab;

        CustomButton Button(string field)
        {
            try { return AccessTools.Field(typeof(MegabonkTogether.Scripts.NetworkMenuTab), field)?.GetValue(tab) as CustomButton; }
            catch { return null; }
        }

        void Record(string name, string fragment)
        {
            if (SceneHasLabel(fragment)) present.Add(name); else missing.Add(name);
        }

        Record("player name field", "Player Name");

        // The world picker lives on the Friendlies screen, so the check clicks through to it
        // the same way a player would rather than only opening the front screen.
        var friendlies = Button("friendliesButton");
        if (friendlies == null) missing.Add("friendlies button");
        else friendlies.OnClick();
        yield return null;
        yield return null;

        Record("world picker", "World to host");
        Record("new world choice", "New World");

        // Press Host for real: it threw on an unsupported GameObject constructor once, and a
        // button whose handler dies looks exactly like a button that is not clickable.
        var hostButton = Button("hostButton");
        if (hostButton == null) missing.Add("host button");
        else
        {
            try
            {
                hostButton.OnClick();
                present.Add("host button click");
            }
            catch (Exception ex)
            {
                missing.Add("host button click");
                Plugin.Log.LogError($"BONKLINK_UI_FAIL: pressing Host threw: {ex}");
            }
        }
        yield return null;
        yield return null;

        var back = Button("friendliesBackButton");
        if (back != null) back.OnClick();
        yield return null;

        var options = Button("netplayOptionsButton");
        if (options == null) missing.Add("netplay options button");
        else options.OnClick();
        yield return null;
        yield return null;

        Record("continue-world toggle", "Continue Saved Co-op World");
        Record("shared experience toggle", "Shared Experience");

        Plugin.Log.LogInfo($"BONKLINK_UI_MENU: built={modalObject != null} present={string.Join("|", present)} missing={string.Join("|", missing)}");

        // Leave the menu as we found it so the rest of the run is unaffected.
        try
        {
            GameObject.Destroy(modalObject);
            Plugin.Instance.NetworkTab = null;
        }
        catch { }
    }

    /// <summary>True when any label anywhere on screen contains this text.</summary>
    private static bool SceneHasLabel(string fragment)
    {
        try
        {
            foreach (var go in Il2CppFindHelper.FindAllGameObjects())
            {
                if (go == null) continue;
                foreach (var text in go.RuntimeGetComponents<TMPro.TextMeshProUGUI>())
                {
                    var value = text?.text;
                    if (!string.IsNullOrEmpty(value) && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"BONKLINK_UI_FAIL: scanning labels threw: {ex.Message}");
        }

        return false;
    }

    /// <summary>Opens the pause menu as the host and exercises the save button on it.</summary>
    private void CheckPauseUi()
    {
        try
        {
            var pause = UiManager.Instance?.pause;
            if (pause == null)
            {
                Plugin.Log.LogWarning("BONKLINK_UI_FAIL: no pause screen available");
                return;
            }

            pause.Pause();
            CoroutineRunner.Instance.Run(InspectPauseUi(pause));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"BONKLINK_UI_FAIL: opening the pause screen threw: {ex}");
        }
    }

    private System.Collections.IEnumerator InspectPauseUi(PauseUi pause)
    {
        yield return null;
        yield return null;

        try
        {
            var buttons = Il2CppFindHelper.RuntimeGetComponentsInChildren<MegabonkTogether.Scripts.Button.CustomButton>(pause);
            var saveButton = buttons.FirstOrDefault(b => b != null && b.gameObject.name == "BonkLinkSaveWorldButton");

            if (saveButton == null)
            {
                Plugin.Log.LogWarning($"BONKLINK_UI_PAUSE: save button missing (custom buttons found: {buttons.Length})");
            }
            else
            {
                var label = saveButton.gameObject.GetComponent<ButtonTextWrapper>();
                var before = label?.t_text?.text ?? "?";
                saveButton.OnClick();
                var after = label?.t_text?.text ?? "?";
                Plugin.Log.LogInfo($"BONKLINK_UI_PAUSE: save button present, label '{before}' -> '{after}'");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"BONKLINK_UI_FAIL: inspecting the pause screen threw: {ex}");
        }

        try { pause.Resume(); } catch { }
    }

    private static string SafeStageName()
    {
        try { return MapController.currentStage?.name ?? "?"; } catch { return "?"; }
    }

    /// <summary>
    /// Drives and reports the two paths the automated runs had never covered: a real stage
    /// boss, and a revive completed by actually killing the ghost rather than injecting its
    /// death event. The host drives; both sides report what they see so desync is visible.
    /// </summary>
    private void DriveBossTest(bool host, bool client, float live)
    {
        var enemyManager = Plugin.Services.GetRequiredService<IEnemyManagerService>();
        var players = Plugin.Services.GetRequiredService<IPlayerManagerService>();

        // Both sides report the boss they can see, so a divergence shows up in the logs.
        var visibleBoss = enemyManager.GetAllSpawnedEnemies()
            .Where(p => p.Value != null)
            .FirstOrDefault(p => SafeIsBoss(p.Value));

        if (visibleBoss.Value != null)
        {
            Plugin.Log.LogInfo($"BONKLINK_BOSS_STATE: id={visibleBoss.Key} hp={visibleBoss.Value.hp:F0}/{visibleBoss.Value.maxHp:F0} dead={EnemyManager.Instance.stageBossIsDead}");
        }

        if (!host) return;

        if (!bossSpawned && live > 20)
        {
            bossSpawned = true;
            SpawnStageBoss();
            return;
        }

        if (bossSpawned && bossId != 0 && !bossCheckpointed && live > 32)
        {
            bossCheckpointed = true;
            CheckBossCheckpoint(enemyManager);
        }

        // Real damage through the mod's own damage path, in bites, until the boss dies.
        if (bossSpawned && bossId != 0 && !bossDead && live > 40 && elapsed - bossDamageAt > 1f)
        {
            bossDamageAt = elapsed;
            var boss = enemyManager.GetEnemyById(bossId);

            if (boss == null || boss.IsDead())
            {
                bossDead = true;
                Plugin.Log.LogInfo($"BONKLINK_BOSS_DEAD: id={bossId} stageBossIsDead={EnemyManager.Instance.stageBossIsDead}");
            }
            else
            {
                var before = boss.hp;
                var bite = Math.Max(1f, boss.maxHp / 4f);
                boss.Damage(new DamageContainer(0, MegabonkTogether.Patches.Enemies.EnemyPatch.AllowedDamageSource.First()) { damage = bite });
                Plugin.Log.LogInfo($"BONKLINK_BOSS_HIT: id={bossId} {before:F0} -> {boss.hp:F0} (-{bite:F0})");
            }
        }

        // The ghost is killed with real damage; the revive must follow from that alone.
        if (coffinUsed && !realGhostKilled && live > 55 && elapsed - ghostHuntAt > 1f)
        {
            ghostHuntAt = elapsed;
            var ghost = enemyManager.GetAllSpawnedEnemies()
                .Where(p => p.Value != null)
                .FirstOrDefault(p => enemyManager.GetReviverEnemy_Name(p.Value) != null);

            if (ghost.Value != null)
            {
                if (ghost.Value.IsDead())
                {
                    realGhostKilled = true;
                    Plugin.Log.LogInfo($"BONKLINK_GHOST_KILLED_FOR_REAL: id={ghost.Key}");
                }
                else
                {
                    var before = ghost.Value.hp;
                    MyTime.Unpause();
                    ghost.Value.Damage(new DamageContainer(0, MegabonkTogether.Patches.Enemies.EnemyPatch.AllowedDamageSource.First()) { damage = Math.Max(1f, ghost.Value.maxHp / 4f) });
                    Plugin.Log.LogInfo($"BONKLINK_GHOST_HIT: id={ghost.Key} {before:F0} -> {ghost.Value.hp:F0}");
                }
            }
        }

        foreach (var player in players.GetAllPlayersExceptLocal())
        {
            var net = players.GetNetPlayerByNetplayId(player.ConnectionId);
            if (net?.Inventory?.playerHealth == null) continue;
            Plugin.Log.LogInfo($"BONKLINK_REMOTE_HEALTH: {player.Name} hp={net.Inventory.playerHealth.hp} visible={net.gameObject.activeSelf}");
        }
    }

    private static bool SafeIsBoss(Enemy enemy)
    {
        try { return enemy.IsBoss(); } catch { return false; }
    }

    /// <summary>Forces the stage's own boss to appear rather than waiting out the stage timer.</summary>
    private void SpawnStageBoss()
    {
        try
        {
            var timeline = EnemyManager.Instance.summonerController?.timeline;
            var bossData = timeline?.boss;

            if (bossData == null)
            {
                Plugin.Log.LogWarning("BONKLINK_BOSS_FAIL: this stage timeline has no boss");
                return;
            }

            var player = GameManager.Instance.player.transform.position;
            var where = player + new Vector3(12f, 0f, 12f);
            var boss = EnemyManager.Instance.SpawnBoss(bossData.enemyName, 1, EEnemyFlag.StageBoss, where, 1f);

            if (boss == null)
            {
                Plugin.Log.LogWarning($"BONKLINK_BOSS_FAIL: {bossData.enemyName} did not spawn");
                return;
            }

            var manager = Plugin.Services.GetRequiredService<IEnemyManagerService>();
            bossId = manager.GetEnemyByReference(boss).Key;
            Plugin.Log.LogInfo($"BONKLINK_BOSS_SPAWNED: {bossData.enemyName} id={bossId} hp={boss.hp:F0}/{boss.maxHp:F0} isBoss={boss.IsBoss()}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"BONKLINK_BOSS_FAIL: {ex}");
        }
    }

    /// <summary>A checkpoint taken with a boss alive must carry that boss's health.</summary>
    private void CheckBossCheckpoint(IEnemyManagerService enemyManager)
    {
        try
        {
            var boss = enemyManager.GetEnemyById(bossId);
            if (boss == null)
            {
                Plugin.Log.LogWarning("BONKLINK_BOSS_SAVE_FAIL: boss gone before the checkpoint");
                return;
            }

            var liveHp = boss.hp;
            var worldSaves = Plugin.Services.GetRequiredService<IWorldSaveService>();

            if (!worldSaves.SaveNow("boss probe"))
            {
                Plugin.Log.LogWarning("BONKLINK_BOSS_SAVE_FAIL: checkpoint not written");
                return;
            }

            var reloaded = new WorldSaveStore(worldSaves.SaveDirectory).ListReadable().FirstOrDefault();
            var saved = reloaded?.Enemies.FirstOrDefault(e => e.NetworkId == bossId);

            if (saved == null)
            {
                Plugin.Log.LogWarning($"BONKLINK_BOSS_SAVE_FAIL: boss {bossId} missing from the reloaded checkpoint");
                return;
            }

            var matches = Math.Abs(saved.Hp - liveHp) < 1f;
            var livePos = boss.transform.position;
            var placed = Math.Abs(saved.Pose.X - livePos.x) < 1f && Math.Abs(saved.Pose.Z - livePos.z) < 1f;
            Plugin.Log.LogInfo($"BONKLINK_BOSS_SAVED: id={bossId} live={liveHp:F1} saved={saved.Hp:F1} isBoss={saved.IsBoss} maxHp={saved.MaxHp:F0} matches={matches} livePos=({livePos.x:F1},{livePos.y:F1},{livePos.z:F1}) savedPos=({saved.Pose.X:F1},{saved.Pose.Y:F1},{saved.Pose.Z:F1}) placed={placed}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"BONKLINK_BOSS_SAVE_FAIL: {ex}");
        }
    }

    /// <summary>
    /// Writes a co-op checkpoint from the running game, reads it back through a fresh store
    /// as a restart would, and then applies it to the live run. Applying a checkpoint that
    /// matches the current state must change nothing, so any drift shows up in the log.
    /// </summary>
    private void CheckWorldSave()
    {
        try
        {
            var worldSaves = Plugin.Services.GetRequiredService<IWorldSaveService>();
            var players = Plugin.Services.GetRequiredService<IPlayerManagerService>();
            var inventory = GameManager.Instance.player.inventory;
            var goldBefore = inventory.goldInt;
            var hpBefore = inventory.playerHealth.hp;
            Enemy effectProbe = null;
            if (Environment.GetCommandLineArgs().Contains("--bonklink.checkpoint"))
            {
                PickupManager.Instance.SpawnPickup(Assets.Scripts.Inventory__Items__Pickups.Pickups.EPickup.Gold,
                    GameManager.Instance.player.transform.position + new Vector3(50, 1, 50), 731, false);
                effectProbe = Plugin.Services.GetRequiredService<IEnemyManagerService>().GetAllSpawnedEnemies().Select(e => e.Value).FirstOrDefault();
                if (effectProbe != null)
                    effectProbe.AddDebuffImplementation(new Assets.Scripts.Game.Combat.EnemyDebuffs.AddDebuffContainer(
                        Assets.Scripts.Game.Combat.EnemyDebuffs.EDebuff.Poison, new DamageContainer(3, "checkpoint probe"), 20, 3));
            }

            if (!worldSaves.SaveNow("smoke probe"))
            {
                Plugin.Log.LogWarning("BONKLINK_WORLDSAVE_FAIL: checkpoint not written");
                return;
            }

            var store = new WorldSaveStore(worldSaves.SaveDirectory);
            var reloaded = store.ListReadable().FirstOrDefault();

            if (reloaded == null)
            {
                Plugin.Log.LogWarning("BONKLINK_WORLDSAVE_FAIL: nothing readable on disk");
                return;
            }

            var mine = reloaded.Players.FirstOrDefault(p => players.IsLocalConnectionId(p.ConnectionId));
            if (Environment.GetCommandLineArgs().Contains("--bonklink.checkpoint"))
            {
                var effectSave = reloaded.Enemies.FirstOrDefault(e => e.Debuffs.Any(d => d.Kind == 1));
                var pickupOk = reloaded.Pickups.Any(p => p.Value == 731);
                var effectsOk = false;
                if (effectProbe != null && effectSave != null)
                {
                    effectProbe.ClearAllDebuffs();
                    EnemyEffects.Apply(effectProbe, effectSave);
                    var after = new SavedEnemy();
                    EnemyEffects.Capture(effectProbe, after);
                    effectsOk = after.Debuffs.Any(d => effectSave.Debuffs.Any(s => s.Kind == d.Kind && s.Ticks == d.Ticks && s.Stacks == d.Stacks));
                }
                Plugin.Log.LogInfo($"BONKLINK_EXTRA_CHECKPOINT: pickup={pickupOk} effect={effectsOk}");
            }
            var weapons = mine == null ? 0 : mine.Weapons.Count();
            var tomes = mine == null ? 0 : mine.Tomes.Count();
            var items = mine == null ? 0 : mine.Items.Values.Sum();
            var bosses = reloaded.Enemies.Count(e => e.IsBoss);
            var bossHp = reloaded.Enemies.Where(e => e.IsBoss).Select(e => e.Hp).DefaultIfEmpty(0).Max();
            var gaps = reloaded.MissingState.Count == 0 ? "none" : string.Join("|", reloaded.MissingState);

            Plugin.Log.LogInfo($"BONKLINK_WORLDSAVE: rev={reloaded.Revision} resumable={reloaded.Resumable} stage={reloaded.Stage} t={reloaded.ElapsedSeconds:F0}s players={reloaded.Players.Count} enemies={reloaded.Enemies.Count} bosses={bosses} maxBossHp={bossHp:F0} gaps={gaps}");
            Plugin.Log.LogInfo($"BONKLINK_WORLDSAVE_SELF: gold={mine?.Gold} hp={mine?.Hp}/{mine?.MaxHp} level={mine?.Level} weapons={weapons} tomes={tomes} items={items}");

            var matched = reloaded.Players.Where(p => players.GetPlayer(p.ConnectionId) != null).ToArray();
            var applied = WorldApply.ApplyPlayers(matched, players);

            var goldAfter = inventory.goldInt;
            var hpAfter = inventory.playerHealth.hp;
            var stable = goldAfter == goldBefore && hpAfter == hpBefore;

            Plugin.Log.LogInfo($"BONKLINK_WORLDSAVE_APPLY: restored={applied}/{matched.Length} gold {goldBefore}->{goldAfter} hp {hpBefore}->{hpAfter} unchanged={stable}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"BONKLINK_WORLDSAVE_FAIL: {ex}");
        }
    }
}
#endif
