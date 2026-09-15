// BonkLink edition, 2026-09-15. GPL-2.0; see LICENSE.
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Camera;
using HarmonyLib;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// Stops a recycled enemy throwing halfway through being set up.
    ///
    /// The minimap keeps its icons in a dictionary keyed by the enemy itself and adds to it with
    /// <c>Add</c>, which throws if the key is already there. Enemies come from a pool, so the same
    /// Enemy object is handed out again and again, and its entry is only cleared when the game's
    /// own death path runs. (The exception names a Transform because that is how an Enemy prints
    /// itself; the key is the Enemy.) In a co-op session enemies also leave play by other routes -- the host
    /// recycling them, a stage ending, a despawn driven over the network -- and after any of
    /// those the stale icon is still in the dictionary. The next enemy handed that transform
    /// then threw:
    ///
    ///   System.ArgumentException: An item with the same key has already been added.
    ///     Key: Enemy1(Clone) (UnityEngine.Transform)
    ///     at MinimapCamera.OnEnemySpawn -> Enemy.InitEnemy
    ///
    /// The throw happens **inside InitEnemy**, so the enemy is left half set up: it exists, it is
    /// active, and whatever InitEnemy had not reached yet was never done. That is the most
    /// plausible cause found so far for enemies that drift, slide or behave as though they were
    /// never given their state -- a complaint that has outlived several other explanations.
    ///
    /// Clearing the stale entry first is enough. The icon that replaces it is the correct one for
    /// the enemy now using that transform, which is what the dictionary was meant to hold.
    /// </summary>
    [HarmonyPatch(typeof(MinimapCamera))]
    internal static class MinimapCameraPatches
    {
        private static int cleared;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MinimapCamera.OnEnemySpawn))]
        private static void OnEnemySpawn_Prefix(MinimapCamera __instance, Enemy enemy)
        {
            try
            {
                if (enemy == null) return;

                var icons = __instance.enemyIconDictionary;
                if (icons == null) return;

                if (!icons.ContainsKey(enemy)) return;

                // Through the game's own removal so the old icon object is disposed of the way
                // the game expects, rather than left behind for the minimap to keep drawing.
                __instance.RemoveEnemyMinimapIcon(enemy);

                // Removal is by the game's rules and may not cover an entry left by a route the
                // game does not know about, so make certain the key is gone before it is re-added.
                if (icons.ContainsKey(enemy)) icons.Remove(enemy);

                cleared++;
                if (cleared == 1 || cleared % 50 == 0)
                {
                    Plugin.Log.LogInfo($"Cleared a stale minimap icon from a recycled enemy before it was set up again ({cleared} so far). Without this the enemy is left half-initialised.");
                }
            }
            catch (System.Exception ex)
            {
                // Never stop an enemy spawning over a minimap icon.
                Plugin.Log.LogWarning($"Could not clear a stale minimap icon: {ex.Message}");
            }
        }
    }
}
