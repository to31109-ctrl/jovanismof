using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Managers;
using Il2CppInterop.Runtime;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using UnityEngine;

namespace MegabonkTogether.Scripts.Interactables
{
    public class InteractableReviver : BaseInteractable
    {
        private bool hasInteracted = false;
        private bool charging;
        private bool sawKeyHeld;
        private float heldSeconds;
        private float chargingSeconds;
        private GameObject chargeFx;
        private GameObject explodeFx;
        private Enemy spawned;
        private Material customMaterial;
        private Il2CppSystem.Action<Enemy, DamageContainer> enemyDiedDelegate;
        private ISynchronizationService synchronizationService;
        private IPlayerManagerService playerManagerService;
        private IEnemyManagerService enemyManagerService;
        private uint reviverId;
        private uint ownerId;

        /// <summary>
        /// Every coffin currently waiting for its player. Kept as a list of our own rather than
        /// searched for, because the generic object-find overloads do not exist under IL2CPP.
        /// </summary>
        private static readonly System.Collections.Generic.List<InteractableReviver> Waiting = new();


        protected void Awake()
        {
            synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
            playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();
            enemyManagerService = Plugin.Services.GetService<IEnemyManagerService>();
        }

        public void SetSpawnedEnemy(Enemy enemy)
        {
            spawned = enemy;

            var renderers = Il2CppFindHelper.RuntimeGetComponentsInChildren<Renderer>(enemy.gameObject, true);

            foreach (var r in renderers)
            {
                if (r.gameObject.name == "Render")
                {
                    Material[] mats = [customMaterial];
                    Il2CppFindHelper.RuntimeSetSharedMaterials(r, mats);

                    enemy.enemyData.material = customMaterial;
                }
            }
        }

        public void Initialize(GameObject chargeFxPrefab, GameObject explodeFxPrefab, Material mat, uint reviverid, uint ownerId)
        {
            reviverId = reviverid;
            this.ownerId = ownerId;

            chargeFx = GameObject.Instantiate(chargeFxPrefab, this.transform);
            chargeFx.SetActive(false);
            explodeFx = GameObject.Instantiate(explodeFxPrefab, this.transform);
            explodeFx.SetActive(false);

            customMaterial = mat;

            enemyDiedDelegate = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Enemy, DamageContainer>>(OnEnemyDied);
            Enemy.A_EnemyDied += enemyDiedDelegate;
        }

        public string GetFullName()
        {
            return $"{playerManagerService.GetPlayer(ownerId).Name} Ghost";
        }

        public override bool CanInteract()
        {
            return !hasInteracted;
        }

        private void OnEnemyDied(Enemy enemy, DamageContainer dc)
        {
            if (spawned == null || enemy != spawned)
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;

            if (!isServer)
            {
                return;
            }

            CompleteRevive();
        }

        /// <summary>
        /// Brings the owner back and clears the coffin away. Shared by the ghost being killed
        /// and by the area boss dying, so both routes leave exactly the same state behind.
        /// Host only; it is the host that decides a player is alive again.
        /// </summary>
        private void CompleteRevive()
        {
            Waiting.Remove(this);

            synchronizationService.OnRespawn(ownerId, FindStandingPosition());

            if (spawned != null) enemyManagerService.RemoveReviverEnemy_Name(spawned);

            Enemy.A_EnemyDied -= enemyDiedDelegate;
            GameObject.Destroy(this.gameObject);
        }

        /// <summary>
        /// A fixed two metres above the coffin drops the player into the air whenever the
        /// coffin is standing on anything raised, so the ground underneath is looked for and
        /// the player is put just above it.
        /// </summary>
        private Vector3 FindStandingPosition()
        {
            var origin = this.gameObject.transform.position;
            try
            {
                // Started above the coffin so the cast cannot begin inside the floor itself.
                if (Physics.Raycast(origin + new Vector3(0, 3f, 0), Vector3.down, out var hit, 30f))
                {
                    return hit.point + new Vector3(0, 0.5f, 0);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not find the ground under a revive; using the coffin ({ex.Message})");
            }
            return origin + new Vector3(0, 0.5f, 0);
        }

        /// <summary>
        /// Killing the area boss brings back everyone still waiting, so a ghost that cannot be
        /// reached or never appeared does not strand a player for the rest of the run.
        /// </summary>
        public static void ReviveEveryoneWaiting()
        {
            if (Waiting.Count == 0) return;

            // Copied first: completing a revive removes it from the list.
            foreach (var coffin in Waiting.ToArray())
            {
                if (coffin == null) { Waiting.Remove(coffin); continue; }
                try { coffin.CompleteRevive(); }
                catch (System.Exception ex) { Plugin.Log.LogError($"Reviving a player on boss death failed: {ex}"); }
            }
        }

        public static void ForgetAllWaiting() => Waiting.Clear();

        public override string GetInteractString()
        {
            if (!charging) return "REVIVE!";
            var required = RequiredHoldSeconds;
            if (required <= 0f) return "REVIVE!";
            var left = System.Math.Max(0f, required - heldSeconds);
            return $"KEEP HOLDING... {left:F1}s";
        }

        /// <summary>
        /// Holding, rather than tapping, so a player cannot start a revive by brushing past the
        /// coffin mid-fight. The hold is abandoned if they let go.
        /// </summary>
        private void Update()
        {
            if (!charging || hasInteracted) return;

            var required = RequiredHoldSeconds;
            if (required <= 0f) { BeginRevive(); return; }

            var holding = false;
            try
            {
                holding = BepInEx.Unity.IL2CPP.UnityEngine.Input.GetKeyInt(
                    (BepInEx.Unity.IL2CPP.UnityEngine.KeyCode)KeyCode.E);
            }
            catch (System.Exception ex)
            {
                // Never let a key that cannot be read make reviving impossible.
                Plugin.Log.LogWarning($"Could not read the revive hold; reviving immediately ({ex.Message})");
                BeginRevive();
                return;
            }

            if (holding)
            {
                sawKeyHeld = true;
                heldSeconds += Time.deltaTime;
                if (heldSeconds >= required) BeginRevive();
                return;
            }

            // The interact key is rebindable. If holding was never seen at all shortly after the
            // press, this player's key is not the one being watched, so the revive goes ahead
            // rather than being silently impossible for them.
            chargingSeconds += Time.deltaTime;
            if (!sawKeyHeld && chargingSeconds > 1.5f)
            {
                Plugin.Log.LogInfo("Revive hold could not be followed on this input; reviving on the press instead.");
                BeginRevive();
                return;
            }

            heldSeconds = 0f;
        }

        private static float RequiredHoldSeconds
        {
            get
            {
                try { return Configuration.ModConfig.ReviveHoldSeconds.Value; }
                catch { return 0f; }
            }
        }

        public override bool Interact()
        {
            if (hasInteracted) return false;

            // A zero hold keeps the old behaviour of reviving on the press.
            if (RequiredHoldSeconds > 0f && !charging)
            {
                charging = true;
                heldSeconds = 0f;
                chargingSeconds = 0f;
                sawKeyHeld = false;
                return false;
            }

            BeginRevive();
            return true;
        }

        private void BeginRevive()
        {
            if (hasInteracted) return;
            hasInteracted = true;
            charging = false;

            this.gameObject.GetComponent<Collider>().enabled = false;

            var meshRenderers = Il2CppFindHelper.RuntimeGetComponentsInChildren<MeshRenderer>(this.gameObject, true);
            foreach (var mr in meshRenderers)
            {
                mr.enabled = false;
            }

            var transforms = Il2CppFindHelper.RuntimeGetComponentsInChildren<Transform>(this.gameObject, true);

            foreach (var t in transforms)
            {
                if (t.gameObject.name.Contains("Beam"))
                {
                    t.gameObject.SetActive(false);
                }
            }

            chargeFx.SetActive(true);

            var isHost = synchronizationService.IsServerMode() ?? false;

            if (!isHost) return;

            if (!Waiting.Contains(this)) Waiting.Add(this);

            // Holding the key is the revive. The ghost that used to have to be killed first is
            // off by default: it could fail to spawn on a full map, it was built from a flying
            // enemy that drifted away, and it left the player who was down waiting on a fight
            // they could not take part in.
            if (Configuration.ModConfig.ReviveNeedsGhost.Value)
            {
                CoroutineRunner.Instance.Run(SpawnEnemy());
                return;
            }

            CompleteRevive();
        }

        private IEnumerator SpawnEnemy()
        {
            yield return new WaitForSeconds(0.5f);

            chargeFx.SetActive(false);

            // Tried more than once: a spawn can still come back empty while the pool is busy,
            // and a player left with no ghost has no way back for the rest of the run.
            Enemy enemy = null;
            for (var attempt = 0; attempt < 5 && enemy == null; attempt++)
            {
                Plugin.Instance.CurrentReviver = reviverId;
                Plugin.Instance.CurrentReviverOwner = ownerId;
                try
                {
                    enemy = EnemyManager.Instance.SpawnBoss(Actors.Enemies.EEnemy.GhostGrave4, 0, EEnemyFlag.Boss, this.transform.position, 2f);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"Revive ghost spawn attempt {attempt + 1} failed: {ex.Message}");
                }
                finally
                {
                    Plugin.Instance.CurrentReviver = null;
                    Plugin.Instance.CurrentReviverOwner = null;
                }

                if (enemy == null) yield return new WaitForSeconds(0.5f);
            }

            if (enemy == null)
            {
                // Better to hand the player straight back than to strand them by a coffin that
                // will never produce anything to kill.
                Plugin.Log.LogError("The revive ghost could not be spawned; reviving the player directly instead.");
                CompleteRevive();
                yield break;
            }

            enemyManagerService.AddReviverEnemy_Name(enemy, GetFullName());

            var renderers = Il2CppFindHelper.RuntimeGetComponentsInChildren<Renderer>(enemy.gameObject, true);

            foreach (var r in renderers)
            {
                if (r.gameObject.name == "Render")
                {
                    Material[] mats = [customMaterial];
                    Il2CppFindHelper.RuntimeSetSharedMaterials(r, mats);

                    enemy.enemyData.material = customMaterial;
                }
            }

            // GhostGrave4 is a flying enemy, so left alone the ghost drifts upward and away
            // from the people trying to kill it to get their friend back. The hover offset is
            // per enemy; EnemyData.isFlying is a ScriptableObject shared by every enemy of this
            // species, so switching that off would ground them all for the rest of the run.
            if (!Configuration.ModConfig.ReviveGhostFlies.Value)
            {
                try
                {
                    var movement = enemy.GetComponent<global::Actors.Enemies.EnemyMovementRb>();
                    if (movement != null) movement.flyingOffset = Vector3.zero;
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Could not ground the revive ghost: {ex.Message}"); }
            }

            spawned = enemy;

            yield return null;
        }
    }
}
