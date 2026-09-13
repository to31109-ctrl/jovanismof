// BonkLink edition addition, 2026-09-12. GPL-2.0; see LICENSE.
namespace MegabonkTogether.Common.Networking;
public sealed class SnapshotBaseline<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> sent = new();
    public void Clear() => sent.Clear();
    public List<TValue> Collect(IReadOnlyDictionary<TKey, TValue> current, Func<TValue, TValue, bool> changed, bool refresh)
    {
        var updates = new List<TValue>();
        foreach (var pair in current)
        {
            if (refresh || !sent.TryGetValue(pair.Key, out var previous) || changed(previous, pair.Value))
            {
                updates.Add(pair.Value);
                sent[pair.Key] = pair.Value;
            }
        }
        foreach (var removed in sent.Keys.Where(k => !current.ContainsKey(k)).ToArray()) sent.Remove(removed);
        return updates;
    }
}
