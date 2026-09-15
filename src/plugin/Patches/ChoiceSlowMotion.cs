// BonkLink edition, 2026-09-15. GPL-2.0; see LICENSE.
using Assets.Scripts.Utility;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    /// <summary>
    /// Slows the world while a player is picking an upgrade, instead of stopping it.
    ///
    /// A shared session used to freeze everybody until every player had chosen. That single
    /// design produced the longest-running fault in this mod: any player whose choice failed to
    /// register -- for any of half a dozen reasons, several of them since fixed -- left everyone
    /// else frozen with nothing to click, and left themselves stranded while the rest of the
    /// party carried on. Waiting on other people is the thing that breaks, so nobody waits any
    /// more. Each player picks at their own pace, the world keeps running, and the run continues
    /// no matter what any one player's choice window does.
    ///
    /// Slow rather than full speed because reading three upgrades while a horde closes in is not
    /// a choice, it is a punishment. This uses the game's own slow-motion, the one already used
    /// elsewhere in the game, so it looks like part of the game rather than something bolted on.
    ///
    /// Re-applied every frame with a short duration rather than set once and cleared. If
    /// anything at all goes wrong -- an exception, a scene change, a window that vanishes
    /// without telling anyone -- the slow motion simply expires on its own within a fraction of
    /// a second. Nothing here can leave a player stuck, which is the whole point of replacing
    /// what came before.
    /// </summary>
    internal static class ChoiceSlowMotion
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>How slow the world runs while somebody is choosing.</summary>
        private const float SlowScale = 0.25f;

        /// <summary>
        /// How long each application lasts. Longer than a frame so the slowdown is steady, short
        /// enough that it is gone almost immediately once the choice is taken.
        /// </summary>
        private const float SlowDuration = 0.5f;

        /// <summary>
        /// The longest the world may run slowly in one stretch.
        ///
        /// The slowdown ends when the last player takes their upgrade, which means it depends on
        /// somebody else again -- and depending on somebody else is what has gone wrong here
        /// every previous time. Slow is survivable where frozen was not, but a player whose
        /// window is lost would still drag the whole run down for ever. After this, full speed
        /// returns regardless and the run carries on.
        /// </summary>
        private const float LongestSlowdownSeconds = 40f;

        /// <summary>True while the world is being held at a crawl for somebody's choice.</summary>
        internal static bool IsSlowing => slowing;

        private static bool slowing;
        private static bool reportedScale;
        private static float slowedFor;
        private static bool gaveUp;

        /// <summary>Called every frame while a session is running.</summary>
        internal static void Tick()
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                if (slowing) Release();
                slowedFor = 0f;
                gaveUp = false;
                return;
            }

            var anyoneChoosing = IsAnyoneChoosing();

            if (!anyoneChoosing)
            {
                if (slowing) Release();
                slowedFor = 0f;
                gaveUp = false;
                return;
            }

            // Whoever is still picking, this player's own window decides whether they can be
            // hurt. Waiting for somebody else is not a reason to be untouchable.
            SetUntouchable(IsLocalChoiceOnScreen());

            if (gaveUp) return;

            slowedFor += UnityEngine.Time.unscaledDeltaTime;
            if (slowedFor >= LongestSlowdownSeconds)
            {
                gaveUp = true;
                Plugin.Log.LogWarning($"The world has run slowly for {LongestSlowdownSeconds:0}s waiting on a choice; back to full speed so the run is not held up.");
                Release();
                return;
            }

            Hold();
        }

        /// <summary>
        /// Anybody in the party, not just this player: the world stays slow until the last
        /// person has taken their upgrade, which is what makes it fair to everyone levelling at
        /// the same moment.
        ///
        /// Read from the lobby state the host broadcasts every tick, so a player who leaves or
        /// whose window closes without a word simply stops counting. Nothing has to arrive for
        /// this to end.
        /// </summary>
        private static bool IsAnyoneChoosing()
        {
            if (IsLocalChoiceOnScreen()) return true;

            try
            {
                var players = playerManagerService?.GetAllPlayers();
                if (players == null) return false;

                var local = playerManagerService.GetLocalPlayer();
                foreach (var player in players)
                {
                    // This player's own entry is a copy of what was last sent; the window above
                    // is the truth for them.
                    if (local != null && player.ConnectionId == local.ConnectionId) continue;
                    if (player.IsChoosing) return true;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not tell whether anyone is still choosing: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Only a window the player can actually see counts. Deliberately not the
        /// "encounter in progress" flag: that one has been seen stuck on, and keying the world's
        /// speed to it would slow the rest of the run to a crawl with nothing on screen to
        /// explain why.
        /// </summary>
        internal static bool IsLocalChoiceOnScreen()
        {
            try
            {
                var window = UiManager.Instance?.encounterWindows?.activeEncounterWindow;
                return window != null && window.gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static void Hold()
        {
            // The game's own slow-motion takes a scale and a duration. Re-applying it keeps the
            // world slow for exactly as long as the window is up and not one frame longer.
            MyTime.SetTimeScale(SlowScale, SlowDuration);

            slowing = true;

            if (!reportedScale)
            {
                reportedScale = true;
                Plugin.Log.LogInfo($"Slowing the world for a choice; the game reports a time scale of {MyTime.timeScale:0.00}.");
            }
        }

        private static void Release()
        {
            slowing = false;
            SetUntouchable(false);
            slowedFor = 0f;

            // Back to normal at once rather than waiting for the slowdown to run out, so taking
            // an upgrade returns the player straight to full speed.
            try { MyTime.SetTimeScale(1f, 0f); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Could not restore the world's speed: {ex.Message}"); }
        }

        private static void SetUntouchable(bool untouchable)
        {
            try
            {
                var player = GameManager.Instance?.player;
                if (player == null) return;

                player.isTeleporting = untouchable;
                Plugin.Instance.IS_MANUAL_INVINCIBLE = untouchable;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Could not change this player's invulnerability for a choice: {ex.Message}");
            }
        }

        /// <summary>Nothing may carry across a run: a session that ends mid-choice ends cleanly.</summary>
        internal static void Reset()
        {
            slowedFor = 0f;
            gaveUp = false;
            reportedScale = false;
            if (!slowing) return;
            Release();
        }
    }
}
