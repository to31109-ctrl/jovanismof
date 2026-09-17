using Assets.Scripts.Actors.Enemies;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegabonkTogether.Scripts.Snapshot
{
    /// <summary>
    /// Moves the host's enemies along the positions it sends. One registry, ticked once a frame.
    ///
    /// This used to be a MonoBehaviour added to every enemy, and that was the single largest
    /// cost a client paid that the host did not. Each one was called by the engine through a
    /// native-to-managed trampoline every frame and then made two calls back to write the
    /// transform: with the final swarm at ~478 enemies that is ~29,000 crossings and ~57,000
    /// writes a second on a client, and none at all on the host, whose enemies are moved by the
    /// game's own code. It is also why a similar machine hosting held 200 frames while a client
    /// held 30.
    ///
    /// Worse, it was added with AddComponent and no check, and enemies are pooled: every time a
    /// client reused one it gained another interpolator, each with its own trampoline, each
    /// writing the transform against the others. The cost grew for the whole run and fastest in
    /// swarms, where recycling is heaviest.
    ///
    /// Now: plain objects in a dictionary keyed by the mod's enemy id, ticked from the one managed
    /// update the mod already runs each frame. Per enemy per frame: zero trampolines and one
    /// combined position-and-rotation write. Registering an id replaces any earlier entry for the
    /// same pooled object, so nothing stacks.
    /// </summary>
    public sealed class EnemyInterpolator
    {
        private const float InterpolationDelay = 0.1f;
        private const int MaxBufferSize = 200;

        private static readonly Dictionary<uint, EnemyInterpolator> registry = new();
        private static readonly List<uint> ids = new();
        private static readonly List<uint> dead = new();
        private static readonly HashSet<string> reportedFaults = new();

        public static int Count => registry.Count;

        public static void Register(uint id, Enemy enemy)
        {
            if (enemy == null) return;

            try
            {
                // A pooled object coming back under a new id must not keep an old mover.
                dead.Clear();
                foreach (var (otherId, other) in registry)
                {
                    if (ReferenceEquals(other.enemy, enemy)) dead.Add(otherId);
                }
                foreach (var otherId in dead) registry.Remove(otherId);

                registry[id] = new EnemyInterpolator(enemy);
            }
            catch (Exception ex)
            {
                ReportOnce($"registering an enemy: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static bool TryGet(uint id, out EnemyInterpolator interpolator) => registry.TryGetValue(id, out interpolator);

        public static void Unregister(uint id) => registry.Remove(id);

        public static void Clear() => registry.Clear();

        /// <summary>Called once per frame from the mod's own update; the only place enemies are moved on a client.</summary>
        public static void TickAll()
        {
            if (registry.Count == 0) return;

            double renderTime = Time.timeAsDouble - InterpolationDelay;

            ids.Clear();
            ids.AddRange(registry.Keys);
            dead.Clear();

            foreach (var id in ids)
            {
                if (!registry.TryGetValue(id, out var mover)) continue;

                try
                {
                    mover.Step(renderTime);
                }
                catch (Exception ex)
                {
                    // A destroyed object throws once here and is dropped -- never once per frame.
                    dead.Add(id);
                    ReportOnce($"moving an enemy: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var id in dead) registry.Remove(id);
        }

        private readonly Enemy enemy;
        private readonly Transform transform;
        private readonly List<EnemySnapshot> buffer = new();
        private float lastHp = float.NaN;

        private EnemyInterpolator(Enemy enemy)
        {
            this.enemy = enemy;
            transform = enemy.transform;
        }

        public void AddSnapshot(in EnemySnapshot snapshot)
        {
            buffer.Add(snapshot);
            if (buffer.Count > MaxBufferSize) buffer.RemoveAt(0);
        }

        private void Step(double renderTime)
        {
            if (buffer.Count < 2) return;

            if (FindSnapshotPair(renderTime, out var older, out var newer))
            {
                // Written only when it changed: this is one more call into the game per enemy.
                if (newer.Hp != lastHp)
                {
                    enemy.hp = newer.Hp;
                    lastHp = newer.Hp;
                }

                if (Vector3.Distance(older.Position, newer.Position) > 2.0f)
                {
                    transform.SetPositionAndRotation(newer.Position, newer.Rotation);
                }
                else
                {
                    float t = Mathf.Clamp01(Factor(renderTime, older.Timestamp, newer.Timestamp));
                    transform.SetPositionAndRotation(
                        Vector3.Lerp(older.Position, newer.Position, t),
                        Quaternion.Slerp(older.Rotation, newer.Rotation, t));
                }
            }

            CleanupOldSnapshots(renderTime);
        }

        private bool FindSnapshotPair(double renderTime, out EnemySnapshot older, out EnemySnapshot newer)
        {
            for (int i = 0; i < buffer.Count - 1; i++)
            {
                if (buffer[i].Timestamp <= renderTime && buffer[i + 1].Timestamp >= renderTime)
                {
                    older = buffer[i];
                    newer = buffer[i + 1];
                    return true;
                }
            }
            older = default;
            newer = default;
            return false;
        }

        private static float Factor(double renderTime, double olderTime, double newerTime)
        {
            var span = newerTime - olderTime;
            return span <= 0 ? 1f : (float)((renderTime - olderTime) / span);
        }

        private void CleanupOldSnapshots(double renderTime)
        {
            int removeCount = 0;
            while (removeCount < buffer.Count - 2 && buffer[removeCount].Timestamp < renderTime - InterpolationDelay)
            {
                removeCount++;
            }
            if (removeCount > 0) buffer.RemoveRange(0, removeCount);
        }

        private static void ReportOnce(string what)
        {
            if (reportedFaults.Add(what))
            {
                Plugin.Log.LogWarning($"Enemy interpolation kept going after a fault while {what}. Reported once; it will not be logged again this session.");
            }
        }
    }
}
