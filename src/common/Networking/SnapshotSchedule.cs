// BonkLink edition addition, 2026-09-12. Distributed under GPL-2.0; see LICENSE.
namespace MegabonkTogether.Common.Networking;
public static class SnapshotSchedule
{
    // A hitch produces one current snapshot, never a burst of identical states.
    public static bool Due(ref float accumulated, float elapsed, float interval)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0 || !float.IsFinite(interval) || interval <= 0) return false;
        accumulated += elapsed;
        if (accumulated < interval) return false;
        accumulated %= interval;
        return true;
    }
}
