using System.Text.Json;

namespace VuePgReplicasAdvBlog.Infrastructure;

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

public static class JobServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and consumer. Handlers use <see cref="AddJobHandler{T, THandler}"/>.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(JobRegistry))) services.AddSingleton(new JobRegistry());
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
