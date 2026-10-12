using System.Text.Json;
using System.Threading.Channels;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure;

// Job payloads. The record's type name is stored as Job.Type, the record as JSON in Job.Payload.
public interface IJob;
public sealed record SendEmail(string To, string Subject, string Html, string Text) : IJob;
public sealed record NotifyComment(Guid CommentId) : IJob;
public sealed record NotifyLike(Guid PostId, Guid LikerId) : IJob;
public sealed record DeleteAssets(string[] Keys) : IJob;

public interface IJobHandler<in T>
{
    Task HandleAsync(T payload, CancellationToken ct);
}

/// <summary>Maps Job.Type to a function that deserializes the payload and runs its handler in a given scope.</summary>
public sealed class JobRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _handlers = new();

    public void Register<T, THandler>() where THandler : IJobHandler<T> =>
        _handlers[typeof(T).Name] = (sp, payload, ct) =>
            sp.GetRequiredService<THandler>().HandleAsync(JsonSerializer.Deserialize<T>(payload)!, ct);

    public bool TryGet(string type, out Func<IServiceProvider, string, CancellationToken, Task> run) =>
        _handlers.TryGetValue(type, out run!);
}

/// <summary>
/// Durable queue: rows in the Jobs table are the source of truth, a bounded channel only wakes the worker
/// instantly. <see cref="EnqueueAsync{T}"/> commits in the caller's transaction (outbox).
/// </summary>
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

    /// <summary>Adds the job, saves it (committing anything else pending on <paramref name="db"/>) and wakes the worker.</summary>
    public async Task<Guid> EnqueueAsync<T>(AppDb db, T payload, DateTime? runAt = null, CancellationToken ct = default) where T : IJob
    {
        var job = Enqueue(db, payload, runAt);
        await db.SaveChangesAsync(ct);
        Wake();
        return job.Id;
    }

    /// <summary>Signals the dispatcher that new jobs are due. Never blocks; repeated signals collapse into one.</summary>
    public void Wake() => _wake.Writer.TryWrite(true);

    /// <summary>Completes when <see cref="Wake"/> was called or <paramref name="timeout"/> elapsed.</summary>
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
