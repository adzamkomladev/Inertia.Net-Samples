using System.Collections.Concurrent;
using Inertia.Net;
using Runwick;

namespace TechLandier.Infrastructure;

/// <summary>
/// At most one admins-pulse reload per window per process: the first event of a burst publishes at once, the rest of
/// the burst folds into one trailing PulseReload job (playbook: throttle "something changed, reload" broadcasts per
/// channel). A scheduled trailing job marks the window closed; if it never runs (its instance went away) the mark
/// expires so the next burst schedules a fresh one. Only the Worker role resolves this gate: it runs the realtime
/// handlers, while Core stays free of Inertia.Net.
/// </summary>
public sealed class AdminPulseGate(IJobOutbox jobs, IInertiaLive live)
{
    const string Channel = RealtimeNames.AdminChannel;
    static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    static readonly TimeSpan TrailingStale = TimeSpan.FromSeconds(10);

    readonly ConcurrentDictionary<string, long> lastPulse = new();
    readonly ConcurrentDictionary<string, long> trailingScheduled = new();

    /// <summary>Publishes the admins pulse now when the window has passed, otherwise schedules one trailing publish.</summary>
    public async Task PublishAsync(CancellationToken ct)
    {
        var now = Environment.TickCount64;
        if (!lastPulse.TryGetValue(Channel, out var last) || now - last >= Window.Ticks)
        {
            await PublishNowAsync(ct);
            return;
        }
        // Inside the window: one trailing publish covers the burst. UniqueWhileActive folds dispatches while the job
        // is queued (a refusal keeps the mark); any other dispatch failure forgets it so the next event retries.
        if (!trailingScheduled.TryGetValue(Channel, out var scheduled) || now - scheduled >= TrailingStale.Ticks)
        {
            trailingScheduled[Channel] = now;
            try { await jobs.DispatchAsync(new PulseReload(), ct); }
            catch (UniquenessConflictException) { }
            catch (RunwickException) { trailingScheduled.TryRemove(Channel, out _); }
        }
    }

    /// <summary>Publishes now, records the time (so the next burst within the window folds into a trailing job) and
    /// marks any scheduled trailing delivered.</summary>
    public async Task PublishNowAsync(CancellationToken ct)
    {
        lastPulse[Channel] = Environment.TickCount64;
        trailingScheduled.TryRemove(Channel, out _);
        await live.PublishAsync(new LiveEvent(RealtimeNames.AdminPulseEvent, LiveChannel.Private(RealtimeNames.AdminChannel)), cancellationToken: ct);
    }

    /// <summary>Clears the window state. Tests call it so parallel fixtures sharing the process do not fold into each other's gate.</summary>
    public void Reset()
    {
        lastPulse.Clear();
        trailingScheduled.Clear();
    }
}
