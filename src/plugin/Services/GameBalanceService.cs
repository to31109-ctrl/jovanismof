using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Managers;
using System.Linq;
using MegabonkTogether.Common.Models;

namespace MegabonkTogether.Services
{
    public interface IGameBalanceService
    {
        public float GetCreditsTimerMultiplier();
        public float GetSpawnMultiplier();
        public float GetEnemyHpMultiplier(EEnemyFlag enemyFlag);
        public float GetFreeChestSpawnRateMultiplier();
        public int GetPickupXpValue();
        public void Initialize();
        public int GetMaxEnemiesSpawnable();
        /// <summary>The hard ceiling: what the game actually allocated. Nothing may pass this.</summary>
        int GetPooledEnemyCeiling();
        public float GetBossLampRequiredCharge();
    }

    public enum DifficultyLevel
    {
        None,
        Duo,
        Trio,
        Quad,
        Five,
        Six
    }

    internal class GameBalanceService(IPlayerManagerService playerManagerService) : IGameBalanceService
    {
        private LobbyScaling Scaling => Plugin.Instance.Mode.Scaling ?? new LobbyScaling();
        private int PlayersCount => System.Math.Clamp(playerManagerService.GetAllPlayers().Count(), 1, 5);
        private static int StageIndex => MapController.runConfig?.mapData.stages.IndexOf(MapController.currentStage) ?? 0;
        private const float baseBossLampInitialChargeTimeSeconds = 3.0f;


        public int GetMaxEnemiesSpawnable() => Scaling.ResolveEnemyCap(PlayersCount, SinglePlayerEnemyCap, PooledEnemyCap);

        /// <summary>
        /// The number of enemies the game has room for, less a little headroom.
        ///
        /// Separate from the spawn cap above because two things are allowed past that cap -- a
        /// boss, and the ghost that lets a downed player back in -- and neither may be allowed
        /// past *this* one. Going beyond what the game allocated does not fail politely: it
        /// hands out an enemy that is still alive and in play, which is then torn from where it
        /// was and rebuilt somewhere else. That is what players describe as enemies flying
        /// around the map.
        /// </summary>
        public int GetPooledEnemyCeiling()
        {
            var pooled = PooledEnemyCap;
            return pooled > 0 ? System.Math.Max(100, pooled - 5) : int.MaxValue;
        }

        /// <summary>
        /// What the game itself allows on screen for one player. Asked of the game rather than
        /// written down here, so it stays right if a patch changes it.
        /// </summary>
        /// <summary>How many enemies the game has actually allocated. Nothing may exceed this.</summary>
        private static int PooledEnemyCap
        {
            get
            {
                try { return EnemyManager.maxNumEnemiesPooled; }
                catch { return 0; }
            }
        }

        private static int SinglePlayerEnemyCap
        {
            get
            {
                try
                {
                    var manager = EnemyManager.Instance;
                    if (manager == null) return 0;

                    // Asked without the mod's own answer in the way. This patch replaces the
                    // game's number with 1000 during a session to keep enemies aggressive, so
                    // asking normally handed this calculation the mod's own invention and called
                    // it "what the game allows for one player".
                    Patches.Enemies.EnemyManagerPatches.AskingTheGameDirectly = true;
                    int native;
                    try { native = manager.GetNumMaxEnemies(); }
                    finally { Patches.Enemies.EnemyManagerPatches.AskingTheGameDirectly = false; }

                    return native > 0 ? native : EnemyManager.maxNumEnemiesPooled;
                }
                catch { return 0; }
            }
        }

        public float GetCreditsTimerMultiplier()
        {
            float baseMultiplier = GetDifficultyLevelByPlayers() switch
            {
                DifficultyLevel.Duo => 1.01f,
                DifficultyLevel.Trio => 1.02f,
                DifficultyLevel.Quad => 1.03f,
                DifficultyLevel.Five => 1.04f,
                DifficultyLevel.Six => 1.05f,
                _ => 1.0f,
            };

            float stageMultiplier = StageIndex switch
            {
                0 => 1.0f,
                1 => 1.05f,
                2 => 1.07f,
                _ => 1.0f
            };

            return baseMultiplier * stageMultiplier;
        }

        public float GetEnemyHpMultiplier(EEnemyFlag enemyFlag)
        {
            var boss = (enemyFlag & EEnemyFlag.AnyBoss) != 0 || enemyFlag == EEnemyFlag.Boss || enemyFlag == EEnemyFlag.FinalBoss;
            return LobbyScaling.Multiplier(PlayersCount, boss ? Scaling.BossHealthPerPlayer : Scaling.EnemyHealthPerPlayer);
        }
        public float GetSpawnMultiplier() => LobbyScaling.Multiplier(PlayersCount, Scaling.SpawnsPerPlayer);

        public float GetFreeChestSpawnRateMultiplier()
        {
            float baseMultiplier = GetDifficultyLevelByPlayers() switch
            {
                DifficultyLevel.Duo => 1.5f,
                DifficultyLevel.Trio => 1.8f,
                DifficultyLevel.Quad => 2.0f,
                DifficultyLevel.Five => 2.2f,
                DifficultyLevel.Six => 2.4f,
                _ => 1f,
            };

            float stageMultiplier = StageIndex switch
            {
                0 => 1.0f,
                1 => 1.1f,
                2 => 1.15f,
                _ => 1.0f
            };

            return baseMultiplier * stageMultiplier;
        }

        public int GetPickupXpValue()
        {
            return PlayersCount switch
            {
                1 => 1,
                >= 2 and <= 4 => 2,
                5 => 3,
                _ => 1
            };
        }

        public void Initialize()
        {
            var creditsMultiplier = GetCreditsTimerMultiplier();
            var enemyHpMultiplier = GetEnemyHpMultiplier(EEnemyFlag.None);
            var chestSpawnMultiplier = GetFreeChestSpawnRateMultiplier();
            var xpValue = GetPickupXpValue();

            Plugin.Log.LogInfo($"[GameBalance] Initialized for {PlayersCount} players, Stage {StageIndex + 1}, Difficulty: {GetDifficultyLevelByPlayers()}");

            // Written down every stage because it is the first thing to check when somebody says
            // the game is unplayably slow. The mod used to impose a flat 1500 here regardless of
            // what the game itself allows, which a strong machine survives and a weaker one does
            // not -- and a machine that cannot keep up also falls behind the host's world.
            Plugin.Log.LogInfo($"[GameBalance] Mobs at once: {GetMaxEnemiesSpawnable()} (the game allows {SinglePlayerEnemyCap} for one player; the pool holds {PooledEnemyCap}).");
            Plugin.Log.LogInfo($"[GameBalance] Credits Timer Multiplier (Disabled): {creditsMultiplier:F2}x");
            Plugin.Log.LogInfo($"[GameBalance] Basic Enemy HP Base Multiplier: {enemyHpMultiplier:F2}x");
            Plugin.Log.LogInfo($"[GameBalance] Free Chest Spawn Rate Multiplier: {chestSpawnMultiplier:F2}x");

            if (Plugin.Instance.Mode.EnabledSharedExperience.HasValue && !Plugin.Instance.Mode.EnabledSharedExperience.Value)
            {
                Plugin.Log.LogInfo($"[GameBalance] XP Value: {xpValue}");
            }
        }

        private DifficultyLevel GetDifficultyLevelByPlayers()
        {
            return PlayersCount switch
            {
                2 => DifficultyLevel.Duo,
                3 => DifficultyLevel.Trio,
                4 => DifficultyLevel.Quad,
                5 => DifficultyLevel.Five,
                6 => DifficultyLevel.Six,
                _ => DifficultyLevel.None
            };
        }

        public float GetBossLampRequiredCharge()
        {
            return GetDifficultyLevelByPlayers() switch
            {
                DifficultyLevel.Duo => baseBossLampInitialChargeTimeSeconds * 2f,
                DifficultyLevel.Trio => baseBossLampInitialChargeTimeSeconds * 2.5f,
                DifficultyLevel.Quad => baseBossLampInitialChargeTimeSeconds * 3f,
                DifficultyLevel.Five => baseBossLampInitialChargeTimeSeconds * 3.5f,
                DifficultyLevel.Six => baseBossLampInitialChargeTimeSeconds * 4f,
                _ => baseBossLampInitialChargeTimeSeconds,
            };
        }

    }
}

