using Assets.Scripts.Inventory__Items__Pickups.Weapons.Attacks;
using Assets.Scripts.Inventory__Items__Pickups.Weapons.Projectiles;
using MegabonkTogether.Patches.Projectiles;
using System.Collections.Generic;
using UnityEngine;

namespace MegabonkTogether.Scripts.Snapshot
{
    /// <summary>
    /// Moves other players' projectiles along the positions the host sends.
    ///
    /// Rewritten after the final-swarm framerate collapse on clients. Two things were wrong,
    /// and both only ever cost the machines that are not the host:
    ///
    /// The transform to move was looked up with three GetComponent calls on every projectile on
    /// every frame -- calls across into the game's runtime, so in a swarm with hundreds of
    /// projectiles in flight that was thousands of them a second for nothing. It is looked up
    /// once now, when the projectile is registered.
    ///
    /// A projectile whose object had already been destroyed stayed registered until the host's
    /// "done" message arrived, and every frame in between the lookup threw a
    /// NullReferenceException that the runtime wrote to disk as a full stack trace, on the main
    /// thread. Two clients' logs were two-thirds stack traces. Nothing here touches an object
    /// without checking it is still alive, a dead one is simply dropped, and no fault is ever
    /// logged more than once.
    ///
    /// And when a projectile is done it is no longer destroyed. Since 5.6.5 these objects come
    /// from the game's pool, and destroying a pooled object poisons the pool: the next shot of
    /// that weapon was handed the corpse. It is finished the way the game finishes its own, so
    /// it goes back into the pool for reuse.
    /// </summary>
    public class ProjectileInterpolator : MonoBehaviour
    {
        private readonly struct Tracked
        {
            public readonly GameObject Root;
            public readonly Transform Moving;
            public Tracked(GameObject root, Transform moving) { Root = root; Moving = moving; }
        }

        private readonly Dictionary<uint, Tracked> activeProjectiles = new();
        private readonly Dictionary<uint, List<ProjectileSnapshot>> snapshotsBuffers = new();
        private readonly List<uint> ids = new();
        private readonly List<uint> dead = new();

        protected float interpolationDelayMs = 0.1f;
        protected int maxBufferSize = 30;

        /// <summary>Snapshots for a projectile that never got registered are dropped after this long.</summary>
        private const double OrphanedBufferSeconds = 5.0;
        private double nextSweep;

        private static readonly HashSet<string> reportedFaults = new();

        protected void Update()
        {
            double renderTime = Time.timeAsDouble - interpolationDelayMs;

            ids.Clear();
            ids.AddRange(activeProjectiles.Keys);
            dead.Clear();

            foreach (var projectileId in ids)
            {
                if (!activeProjectiles.TryGetValue(projectileId, out var tracked)) continue;

                try
                {
                    // A destroyed object reads as null here without a call into the game.
                    if (tracked.Moving == null)
                    {
                        dead.Add(projectileId);
                        continue;
                    }

                    if (!snapshotsBuffers.TryGetValue(projectileId, out var buffer)) continue;
                    if (buffer.Count < 2) continue;

                    if (FindSnapshotPair(buffer, renderTime, out var older, out var newer))
                    {
                        float t = Mathf.Clamp01(CalculateInterpolationFactor(renderTime, older.Timestamp, newer.Timestamp));
                        tracked.Moving.position = Vector3.Lerp(older.Position, newer.Position, t);
                        var olderRot = Quaternion.LookRotation(older.Rotation, Vector3.up);
                        var newerRot = Quaternion.LookRotation(newer.Rotation, Vector3.up);
                        tracked.Moving.rotation = Quaternion.Slerp(olderRot, newerRot, t);
                    }

                    CleanupOldSnapshots(buffer, renderTime);
                }
                catch (System.Exception ex)
                {
                    // Once, never per frame. The per-frame version of this line is what took two
                    // machines to thirty frames a second.
                    dead.Add(projectileId);
                    ReportOnce($"moving a projectile: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var id in dead)
            {
                activeProjectiles.Remove(id);
                snapshotsBuffers.Remove(id);
            }

            if (Time.timeAsDouble >= nextSweep)
            {
                nextSweep = Time.timeAsDouble + 2.0;
                SweepOrphanedBuffers();
            }
        }

        public void UpdateProjectiles(List<ProjectileSnapshot> projectileSnapshots)
        {
            if (projectileSnapshots == null || projectileSnapshots.Count == 0) return;
            foreach (var snapshot in projectileSnapshots) AddSnapshot(snapshot);
        }

        private void AddSnapshot(ProjectileSnapshot snapshot)
        {
            if (!snapshotsBuffers.TryGetValue(snapshot.Id, out var buffer))
            {
                buffer = new List<ProjectileSnapshot>();
                snapshotsBuffers[snapshot.Id] = buffer;
            }
            buffer.Add(snapshot);
            if (buffer.Count > maxBufferSize) buffer.RemoveAt(0);
        }

        /// <summary>
        /// Positions can arrive for a projectile whose spawn never registered here, or keep
        /// arriving after it was dropped. Left alone those buffers accumulated for the whole run.
        /// </summary>
        private void SweepOrphanedBuffers()
        {
            dead.Clear();
            var cutoff = Time.timeAsDouble - OrphanedBufferSeconds;
            foreach (var (id, buffer) in snapshotsBuffers)
            {
                if (activeProjectiles.ContainsKey(id)) continue;
                if (buffer.Count == 0 || buffer[buffer.Count - 1].Timestamp < cutoff) dead.Add(id);
            }
            foreach (var id in dead) snapshotsBuffers.Remove(id);
        }

        private static bool FindSnapshotPair(List<ProjectileSnapshot> buffer, double renderTime, out ProjectileSnapshot older, out ProjectileSnapshot newer)
        {
            older = null;
            newer = null;
            for (int i = 0; i < buffer.Count - 1; i++)
            {
                if (buffer[i].Timestamp <= renderTime && buffer[i + 1].Timestamp >= renderTime)
                {
                    older = buffer[i];
                    newer = buffer[i + 1];
                    return true;
                }
            }
            return false;
        }

        private static float CalculateInterpolationFactor(double renderTime, double olderTime, double newerTime)
        {
            var span = newerTime - olderTime;
            return span <= 0 ? 1f : (float)((renderTime - olderTime) / span);
        }

        private void CleanupOldSnapshots(List<ProjectileSnapshot> buffer, double renderTime)
        {
            int removeCount = 0;
            while (removeCount < buffer.Count - 2 && buffer[removeCount].Timestamp < renderTime - interpolationDelayMs)
            {
                removeCount++;
            }
            if (removeCount > 0) buffer.RemoveRange(0, removeCount);
        }

        public void RegisterProjectile(uint id, GameObject projectile)
        {
            if (projectile == null) return;

            try
            {
                var moving = GetProjectileTransform(projectile);
                if (moving == null) return;
                activeProjectiles[id] = new Tracked(projectile, moving);
                if (!snapshotsBuffers.ContainsKey(id)) snapshotsBuffers[id] = new List<ProjectileSnapshot>();
            }
            catch (System.Exception ex)
            {
                ReportOnce($"registering a projectile: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Ends another player's projectile the way the game ends its own, so a pooled object
        /// goes back into the pool instead of being destroyed underneath it.
        /// </summary>
        public void UnregisterProjectile(uint id)
        {
            if (activeProjectiles.TryGetValue(id, out var tracked))
            {
                activeProjectiles.Remove(id);
                FinishReplica(tracked.Root);
            }
            snapshotsBuffers.Remove(id);
        }

        private static void FinishReplica(GameObject root)
        {
            if (root == null) return;

            try
            {
                var rocket = root.GetComponent<ProjectileRocket>();
                if (rocket != null && rocket.rocket != null)
                {
                    rocket.rocket.ProjectileDone();
                }

                var projectile = root.GetComponent<ProjectileBase>();
                if (projectile != null)
                {
                    // The mod normally refuses ProjectileDone on a replica so a client never
                    // finishes the host's projectile on its own; this is the one deliberate case.
                    ProjectileBasePatches.FinishingReplica = true;
                    try { projectile.ProjectileDone(); }
                    finally { ProjectileBasePatches.FinishingReplica = false; }
                    return;
                }

                root.SetActive(false);
            }
            catch (System.Exception ex)
            {
                ReportOnce($"finishing a projectile: {ex.GetType().Name}: {ex.Message}");
                try { root.SetActive(false); } catch { }
            }
        }

        private static Transform GetProjectileTransform(GameObject projectile)
        {
            var cringe = projectile.GetComponent<ProjectileCringeSword>();
            if (cringe != null && cringe.movingProjectile != null) return cringe.movingProjectile.transform;

            var hero = projectile.GetComponent<ProjectileHeroSword>();
            if (hero != null && hero.movingProjectile != null) return hero.movingProjectile.transform;

            var rocket = projectile.GetComponent<ProjectileRocket>();
            if (rocket != null && rocket.rocket != null) return rocket.rocket.transform;

            return projectile.transform;
        }

        private static void ReportOnce(string what)
        {
            if (reportedFaults.Add(what))
            {
                Plugin.Log.LogWarning($"Projectile interpolation kept going after a fault while {what}. Reported once; it will not be logged again this session.");
            }
        }
    }
}
