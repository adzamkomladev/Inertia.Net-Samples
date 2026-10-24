using System.Text.Json;
using System.Threading.Channels;
using TechLandier.Data;

namespace TechLandier.Infrastructure;

// Job payloads: the type name is stored as Job.Type, the record as JSON in Job.Payload.
public interface IJob;
public sealed record SendEmail(string To, string Subject, string Html, string Text) : IJob;
public sealed record DeleteAssets(string[] Keys) : IJob;

public sealed record WelcomeEmail(Guid UserId) : IJob;
public sealed record OrderPaidEmail(Guid OrderId) : IJob;
public sealed record OrderShippedEmail(Guid OrderId) : IJob;
public sealed record OrderCancelledEmail(Guid OrderId, string Reason) : IJob;
public sealed record OrderRefundEmail(Guid OrderId) : IJob;
public sealed record InstallmentPaidEmail(Guid InstallmentId) : IJob;
public sealed record InstallmentReminderEmail(Guid PlanId, string Kind) : IJob; // Kind: reminder | overdue | statement
public sealed record TicketReplyEmail(Guid TicketId) : IJob;
public sealed record CareDecisionEmail(string Kind, Guid Id) : IJob; // Kind from CareService: warranty-claim | insurance-claim | repair

public interface IJobHandler<in T>
{
    Task HandleAsync(T payload, CancellationToken ct);
}

/// Maps Job.Type to a function that deserializes the payload and runs its handler in a given scope.
public sealed class JobRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _handlers = new();

    public void Register<T, THandler>() where THandler : IJobHandler<T> =>
        _handlers[typeof(T).Name] = async (sp, payload, ct) =>
            await sp.GetRequiredService<THandler>().HandleAsync(JsonSerializer.Deserialize<T>(payload)!, ct);

    public bool TryGet(string type, out Func<IServiceProvider, string, CancellationToken, Task> run) =>
        _handlers.TryGetValue(type, out run!);
}

/// Durable queue: rows in the Jobs table are the source of truth, the channel only wakes the worker early.
/// Enqueue adds the row to the caller's context, so it commits with the business write (outbox).
public sealed class JobQueue(TimeProvider? clock = null)
{
    readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    public Job Enqueue<T>(AppDb db, T payload, DateTime? runAt = null) where T : notnull
    {
        var now = time.GetUtcNow().UtcDateTime;
        var job = new Job
        {
            Type = typeof(T).Name,
            Payload = JsonSerializer.Serialize(payload),
            Status = JobStatus.Pending,
            RunAt = runAt ?? now,
            CreatedAt = now,
        };
        db.Jobs.Add(job);
        return job;
    }

    /// Adds the job, saves it (with anything else pending on the context) and wakes the worker.
    public async Task<Guid> EnqueueAsync<T>(AppDb db, T payload, DateTime? runAt = null, CancellationToken ct = default) where T : IJob
    {
        var job = Enqueue(db, payload, runAt);
        await db.SaveChangesAsync(ct);
        Wake();
        return job.Id;
    }

    /// Tells the dispatcher new jobs are due. Never blocks; repeated signals collapse into one.
    public void Wake() => _wake.Writer.TryWrite(true);

    /// Completes when Wake was called or the timeout elapsed.
    public async Task WaitForWakeAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _wake.Reader.WaitToReadAsync(cts.Token);
            _wake.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // poll interval elapsed
        }
    }
}

public static class JobServiceCollectionExtensions
{
    /// Registers the producer side (main app, worker and cron); consumers are added by the Worker role.
    public static IServiceCollection AddJobQueue(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(JobQueue))) return services;
        if (!services.Any(d => d.ServiceType == typeof(JobRegistry))) services.AddSingleton(new JobRegistry());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JobQueue>();
        return services;
    }

    /// Registers a scoped handler for payload type T.
    public static IServiceCollection AddJobHandler<T, THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler<T>
    {
        var registry = services.FirstOrDefault(d => d.ServiceType == typeof(JobRegistry))?.ImplementationInstance as JobRegistry;
        if (registry is null)
        {
            registry = new JobRegistry();
            services.AddSingleton(registry);
        }
        registry.Register<T, THandler>();
        services.AddScoped<THandler>();
        return services;
    }
}
