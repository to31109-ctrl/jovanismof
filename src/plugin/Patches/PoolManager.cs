using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Inventory__Items__Pickups.Weapons.Attacks;
using Assets.Scripts.Objects.Pooling;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(PoolManager))]
    internal class PoolManagerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        private static int orphaned;

        /// <summary>
        /// Frees a projectile the game cannot put back, instead of leaking it.
        ///
        /// Projectile pools are keyed by weapon, and a machine only has buckets for weapons it
        /// has seen. Another player's build brings projectiles for weapons this player has never
        /// held, and when one of those finishes the game does
        /// <c>pools[weapon].Release(projectile)</c> and throws:
        ///
        ///   KeyNotFoundException: The given key 'Bow' was not present in the dictionary.
        ///     at PoolManager.ReturnProjectile
        ///
        /// The throw happens **after** the projectile has stopped being used and **before** it is
        /// put away, so it is never released and never disabled: it stays in the scene for the
        /// rest of the run. One session's logs carried about a hundred of these on each client
        /// and none on the host, and those two clients were the ones reporting nine frames a
        /// second. Every shot from another player's unfamiliar weapon left something behind.
        ///
        /// A finalizer, because the throw is inside the game's own method -- a prefix cannot
        /// catch it and a postfix never runs. The orphan is destroyed, since nothing owns it any
        /// more, and the exception is swallowed so it stops filling the log it was drowning.
        /// </summary>
        [HarmonyFinalizer]
        [HarmonyPatch(nameof(PoolManager.ReturnProjectile))]
        public static System.Exception ReturnProjectile_Finalizer(System.Exception __exception, UnityEngine.GameObject projectile)
        {
            if (__exception == null) return null;

            try
            {
                if (projectile != null) UnityEngine.Object.Destroy(projectile);
            }
            catch { /* it is already gone; nothing to do */ }

            orphaned++;
            if (orphaned == 1 || orphaned % 100 == 0)
            {
                Plugin.Log.LogInfo($"Freed a projectile this machine has no pool for -- it belongs to another player's weapon ({orphaned} so far). Left alone these stay in the scene and cost frames.");
            }

            return null;
        }

        /// <summary>
        /// Set the ownerId on the WeaponAttack when it's retrieved from the pool (for remote players' attacks)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PoolManager.GetAttack))]
        public static void GetAttack_Postfix(ref WeaponAttack __result, WeaponBase weaponBase)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var weaponBaseData = DynamicData.For(weaponBase);
            var ownerId = weaponBaseData.Get<uint?>("ownerId");

            if (ownerId.HasValue)
            {
                DynamicData.For(__result).Set("ownerId", ownerId.Value);
            }
        }
    }
}