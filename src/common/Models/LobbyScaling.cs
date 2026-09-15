using MemoryPack;

namespace MegabonkTogether.Common.Models;

[MemoryPackable]
public partial class LobbyScaling
{
    // Mob count and mob health both scale, so they multiply: +100% each meant four times the
    // effective difficulty at two players and twenty-five times at five. These defaults keep
    // the curve closer to how party strength actually grows. Hosts can change all of it in
    // the scaling panel, and +100% across the board reproduces the old behaviour.
    public float EnemyHealthPerPlayer { get; set; } = 1f;
    public float BossHealthPerPlayer { get; set; } = 1f;
    /// <summary>
    /// Extra mobs per additional player. Zero: a party faces the same number a single player
    /// does. Adding mobs per player multiplied with the health scaling and with the enemy pool,
    /// and the owner asked for it gone rather than tuned.
    /// </summary>
    public float SpawnsPerPlayer { get; set; } = 0f;
    /// <summary>
    /// Zero means "however many the game itself allows for one player, once per player", which
    /// is what a party actually expects: two players, twice the mobs. Any other value is a flat
    /// cap the host has chosen by hand.
    /// </summary>
    /// <summary>
    /// How many enemies may be alive at once. Defaults to whatever the game allows one player,
    /// which is the only number any of these machines is known to cope with.
    ///
    /// This used to default to a flat 1500, written here and overriding the game's own limit
    /// entirely. On the host's machine that was survivable; on weaker ones it was nine frames a
    /// second, and a client that slow cannot keep up with the host, which is what being "in the
    /// past" is. Nobody asked for five times the mobs -- the request was the opposite, that a
    /// party face what one player faces, with the difficulty coming from health instead.
    /// </summary>
    public int EnemyCap { get; set; } = AutomaticEnemyCap;

    /// <summary>The setting value that means "scale it with the party" rather than a fixed number.</summary>
    public const int AutomaticEnemyCap = 0;

    /// <summary>
    /// The ceiling on active mobs. <paramref name="singlePlayerCap"/> is the game's own limit
    /// for one player, so the party gets that many each.
    /// </summary>
    /// <summary>
    /// The ceiling on active mobs. <paramref name="singlePlayerCap"/> is the game's own limit
    /// for one player and <paramref name="pooledCap"/> is how many enemies the game has
    /// actually allocated.
    ///
    /// Nothing may exceed the pool. The game reuses enemies from a fixed set, so asking for
    /// more active ones than it owns makes it recycle enemies that are still alive: they jump
    /// across the map, and a boss cannot be spawned because there is nothing left to spawn it
    /// from. That is what scaling the cap by player count did without this clamp.
    /// </summary>
    public int ResolveEnemyCap(int players, int singlePlayerCap, int pooledCap)
    {
        // Automatic means the game's own limit for one player, and deliberately not multiplied
        // by the party size. Mobs do not scale with players; health does. Multiplying here was
        // the same per-player scaling that was removed from spawning, wearing a different hat.
        var wanted = EnemyCap != AutomaticEnemyCap
            ? EnemyCap
            : singlePlayerCap <= 0 ? 500 : singlePlayerCap;

        // Headroom kept free so a boss always has something to be spawned from.
        if (pooledCap > 0) wanted = Math.Min(wanted, Math.Max(100, pooledCap - 25));
        return Math.Max(100, wanted);
    }

    public static float Multiplier(int players, float perExtraPlayer) =>
        1f + (Math.Clamp(players, 1, 5) - 1) * Math.Clamp(float.IsFinite(perExtraPlayer) ? perExtraPlayer : 1f, 0f, 3f);

    public bool IsValid() => ValidRate(EnemyHealthPerPlayer) && ValidRate(BossHealthPerPlayer)
        && ValidRate(SpawnsPerPlayer) && (EnemyCap == AutomaticEnemyCap || EnemyCap is >= 100 and <= 2500);
    public static float StepRate(float value, int direction) => Math.Clamp(
        (MathF.Round(value * 100f / 5f) + Math.Sign(direction)) * 5f, 0f, 300f) / 100f;
    private static bool ValidRate(float value) => float.IsFinite(value) && value >= 0 && value <= 3;
}
