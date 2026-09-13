// BonkLink edition addition, 2026-09-13. GPL-2.0; see LICENSE.
using System.Collections.Concurrent;
namespace MegabonkTogether.Common.Networking;
public sealed class GameThreadContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state)> pending = new();
    private int owner;
    public bool IsOwner => owner != 0 && Environment.CurrentManagedThreadId == owner;
    public override void Post(SendOrPostCallback callback, object? state) => pending.Enqueue((callback,state));
    public Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { await action(); completion.TrySetResult(true); }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception ex) { completion.TrySetException(ex); }
        },null);
        return completion.Task;
    }
    public bool DrainOne()
    {
        if (owner == 0) owner = Environment.CurrentManagedThreadId;
        if (!IsOwner) throw new InvalidOperationException("Only the game thread may dispatch callbacks");
        if (!pending.TryDequeue(out var work)) return false;
        var previous = Current;
        SetSynchronizationContext(this);
        try { work.callback(work.state); }
        finally { SetSynchronizationContext(previous); }
        return true;
    }
}
