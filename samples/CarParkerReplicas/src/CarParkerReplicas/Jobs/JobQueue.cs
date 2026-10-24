using System.Text.Json;
using System.Threading.Channels;
using MongoDB.Driver;
using CarParker.Data;

namespace CarParker.Infrastructure;

public interface IJob;
public sealed record SendEmail(string To, string Subject, string Html, string Text) : IJob;
public sealed record DeleteAssets(string[] Keys) : IJob;
public sealed record RefundPaidEmail(Guid RefundId) : IJob;
public sealed record ReservationReminderEmail(Guid ReservationId, Guid OccurrenceId) : IJob;
public sealed record ClaimDecisionEmail(Guid ClaimId, string Decision) : IJob;

/// <summary>MongoDB-backed durable outbox. Pass the caller's session when a domain write must be atomic with its job.</summary>
public sealed class JobQueue
{
    readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public async Task<Job> EnqueueAsync<T>(AppDb db, T payload, IClientSessionHandle? session = null, DateTime? runAt = null, CancellationToken ct = default) where T : IJob
    {
        var job = new Job { Type = typeof(T).Name, Payload = JsonSerializer.Serialize(payload), RunAt = runAt ?? DateTime.UtcNow };
        if (session is null) await db.Jobs.InsertOneAsync(job, cancellationToken: ct);
        else await db.Jobs.InsertOneAsync(session, job, cancellationToken: ct);
        if (session is null && job.RunAt <= DateTime.UtcNow) Wake();
        return job;
    }
    public async Task EnqueueManyAsync<T>(AppDb db, IReadOnlyCollection<T> payloads, CancellationToken ct = default) where T : IJob
    {
        if (payloads.Count == 0) return;
        await db.Jobs.InsertManyAsync(payloads.Select(p => new Job { Type = typeof(T).Name, Payload = JsonSerializer.Serialize(p) }), cancellationToken: ct);
        Wake();
    }

    public void Wake() => wake.Writer.TryWrite(true);
    public async Task WaitForWakeAsync(TimeSpan delay, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(delay);
        try { await wake.Reader.WaitToReadAsync(timeout.Token); wake.Reader.TryRead(out _); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

public interface IJobHandler<in T>
{
    Task HandleAsync(T payload, CancellationToken ct);
}

/// <summary>Maps Job.Type to a function that deserializes the payload and runs its handler in a given scope.</summary>
public sealed class JobRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _handlers = new();

    public void Register<T, THandler>() where THandler : IJobHandler<T> =>
        _handlers[typeof(T).Name] = async (sp, payload, ct) =>
            await sp.GetRequiredService<THandler>().HandleAsync(JsonSerializer.Deserialize<T>(payload)!, ct);

    public bool TryGet(string type, out Func<IServiceProvider, string, CancellationToken, Task> run) =>
        _handlers.TryGetValue(type, out run!);
}

public static class JobServiceCollectionExtensions
{
    /// <summary>Registers the queue and the dispatcher/consumer worker. Handlers use <see cref="AddJobHandler{T, THandler}"/>.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(JobRegistry))) services.AddSingleton(new JobRegistry());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JobQueue>();
        services.AddHostedService<JobWorker>();
        return services;
    }

    /// <summary>Registers a scoped handler for payload type <typeparamref name="T"/>.</summary>
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
