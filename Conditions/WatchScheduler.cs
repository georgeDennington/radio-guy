using System.Collections.Concurrent;
using RadioMan.Dcs;

namespace RadioMan.Conditions;

/// Periodic dispatcher for Watch objects.
///
/// Base tick is 5 s by default — fine for radio-comms cadence. Each Watch
/// has its own Interval; slow watches sit idle on most ticks. New watches
/// can be Register()ed at runtime (e.g. a supervisor watch promoting a pair
/// into close-range monitoring); watches self-deregister when ShouldExit
/// returns true or ExpiresAt passes.
public sealed class WatchScheduler : IDisposable
{
    private readonly IDcsClient _dcs;
    private readonly Func<ScheduledCall, Task> _deliver;
    private readonly TimeSpan _baseTick;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Watch> _watches = new();

    public WatchScheduler(
        IDcsClient dcs,
        Func<ScheduledCall, Task> deliver,
        TimeSpan? baseTick = null)
    {
        _dcs = dcs;
        _deliver = deliver;
        _baseTick = baseTick ?? TimeSpan.FromSeconds(5);
        _ = Task.Run(LoopAsync);
    }

    public void Register(Watch w)
    {
        // First tick fires on the next pass — gives any registering caller a
        // moment to finish its own work before the watch starts running.
        w.NextTickAt = DateTime.UtcNow + _baseTick;
        _watches[w.Id] = w;
    }

    public bool Has(string id) => _watches.ContainsKey(id);

    public void Unregister(string id) => _watches.TryRemove(id, out _);

    public IReadOnlyCollection<Watch> Active => _watches.Values.ToArray();

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(_baseTick, _cts.Token); }
            catch (OperationCanceledException) { return; }

            await TickAsync();
        }
    }

    private async Task TickAsync()
    {
        var now = DateTime.UtcNow;
        var toFire = new List<ScheduledCall>();

        // ConcurrentDictionary enumeration is snapshot-based — watches added
        // mid-tick (e.g. by a supervisor's OnTick) won't be seen until next pass.
        foreach (var (id, watch) in _watches)
        {
            // Self-removal paths first.
            if (now > watch.ExpiresAt)
            {
                _watches.TryRemove(id, out _);
                continue;
            }

            bool shouldExit;
            try { shouldExit = watch.ShouldExit(_dcs); }
            catch (Exception ex)
            {
                Console.WriteLine($"[sched] watch '{id}' ShouldExit threw: {ex.Message}");
                continue;  // skip this tick, try again next pass
            }

            if (shouldExit)
            {
                _watches.TryRemove(id, out _);
                continue;
            }

            if (now < watch.NextTickAt) continue;
            watch.NextTickAt = now + watch.Interval;

            try
            {
                var call = watch.OnTick(_dcs);
                if (call is not null) toFire.Add(call);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sched] watch '{id}' OnTick threw: {ex.Message}");
            }
        }

        // Deliver sequentially. The pipeline's audio lock already serializes,
        // but doing it here too keeps the order deterministic.
        foreach (var call in toFire)
        {
            try { await _deliver(call); }
            catch (Exception ex)
            {
                Console.WriteLine($"[sched] delivery of '{call.Message}' failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
