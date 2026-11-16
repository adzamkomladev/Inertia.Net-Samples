using System.Collections.Concurrent;
using Inkwell.Data;
using Inkwell.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Runwick;

namespace Inkwell.Tests;

/// <summary>
/// Stands in for Runwick in HTTP tests: records every staged or dispatched job, and runs the app's real Runwick
/// handlers on demand. Staged jobs are recorded when staged; RunwickJobOutboxTests covers commit and rollback.
/// </summary>
public sealed class RecordingJobOutbox : IJobOutbox
{
    readonly ConcurrentQueue<IQueueJob> pending = new();
    readonly ConcurrentQueue<IQueueJob> all = new();

    public IReadOnlyList<T> Recorded<T>() where T : IQueueJob => all.OfType<T>().ToList();

    public async Task StageAsync<T>(AppDb db, T job, CancellationToken ct = default, TimeSpan? delay = null) where T : class, IQueueJob
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Staged outside a transaction.");
        await RecordAsync(job);
    }

    public async Task StageManyAsync(AppDb db, IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Staged outside a transaction.");
        foreach (var job in jobs) await RecordAsync(job);
    }

    public async Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, IQueueJob => await RecordAsync(job);

    public async Task DispatchManyAsync(IReadOnlyList<IQueueJob> jobs, CancellationToken ct = default)
    {
        foreach (var job in jobs) await RecordAsync(job);
    }

    async Task RecordAsync(IQueueJob job)
    {
        pending.Enqueue(job);
        all.Enqueue(job);
        await Task.CompletedTask;
    }

    /// <summary>Runs pending jobs whose type name is listed (handlers may queue more) until none are left.</summary>
    public async Task DeliverAsync(IServiceProvider services, params string[] types)
    {
        while (true)
        {
            var due = new List<IQueueJob>();
            var keep = new List<IQueueJob>();
            while (pending.TryDequeue(out var job)) (types.Contains(job.GetType().Name) ? due : keep).Add(job);
            foreach (var job in keep) pending.Enqueue(job);
            if (due.Count == 0) return;
            foreach (var job in due)
            {
                await using var scope = services.CreateAsyncScope();
                var handlerType = HandlerFor(job.GetType());
                var handler = ActivatorUtilities.CreateInstance(scope.ServiceProvider, handlerType);
                var contract = typeof(global::Runwick.IJobHandler<>).MakeGenericType(job.GetType());
                await (Task)contract.GetMethod("HandleAsync")!.Invoke(handler, [job, CancellationToken.None])!;
            }
        }
    }

    static Type HandlerFor(Type job) =>
        typeof(Program).Assembly.GetTypes().SingleOrDefault(t => !t.IsAbstract
            && t.GetInterfaces().Contains(typeof(global::Runwick.IJobHandler<>).MakeGenericType(job)))
        ?? throw new InvalidOperationException($"No handler for {job.Name}.");
}
