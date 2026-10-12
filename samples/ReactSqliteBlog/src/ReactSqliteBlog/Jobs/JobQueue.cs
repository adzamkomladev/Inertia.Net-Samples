using System.Text.Json;
using System.Threading.Channels;
using ReactSqliteBlog.Data;

namespace ReactSqliteBlog.Jobs;

// Job payloads. The type name is stored in Job.Type, the record as JSON in Job.Payload.
public sealed record SendVerificationEmail(Guid UserId);
public sealed record CommentNotification(Guid CommentId);
public sealed record LikeNotification(Guid PostId, Guid LikerId);
public sealed record WeeklyDigestEmail(Guid UserId);

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
            sp.GetRequiredService<THandler>().HandleAsync(JsonSerializer.Deserialize<T>(payload, JobQueue.Json)!, ct);

    public bool TryGet(string type, out Func<IServiceProvider, string, CancellationToken, Task> run) =>
        _handlers.TryGetValue(type, out run!);
}

/// <summary>
/// Durable queue: <see cref="Enqueue{T}"/> adds a Job row to the caller's DbContext so it commits atomically with the
/// domain change (outbox). Call <see cref="Wake"/> after SaveChanges so the dispatcher picks it up immediately instead
/// of on its next poll. <see cref="JobWorker"/> claims due rows and fans them out to concurrent consumers.
/// </summary>
public sealed class JobQueue(TimeProvider time)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
            Payload = JsonSerializer.Serialize(payload, Json),
            Status = JobStatus.Pending,
            RunAt = runAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Jobs.Add(job);
        return job;
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
    /// <summary>Registers the queue and the worker. Handlers are added with <see cref="AddJobHandler{T, THandler}"/>.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(JobRegistry))) services.AddSingleton(new JobRegistry());
        services.AddSingleton<JobQueue>();
        services.AddHostedService<JobWorker>();
        return services;
    }

    /// <summary>Registers a scoped handler for payload type <typeparamref name="T"/>. Call from a feature's Add method.</summary>
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
