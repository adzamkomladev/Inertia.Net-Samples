using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using TechLandier.Infrastructure;
using Inertia.Net;

namespace TechLandier.Caching;

public static class CachingSetup
{
    /// <summary>Registers the shared Inertia prop/SSR store when CACHE_REDIS_URL is configured.</summary>
    public static IServiceCollection AddStoreCache(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var redis = RedisUrlSettings.FromConfiguration(config, "Cache:RedisUrl");
        if (redis is null)
        {
            // In a replicated deployment a process-local prop cache would silently diverge after writes.
            if (ReplicaSetup.Enabled(config))
                throw new InvalidOperationException("CACHE_REDIS_URL is required when Replicas:Enabled is true.");
            return services;
        }

        var options = InertiaCacheStoreOptions.FromConfiguration(config, environment);
        var multiplexer = ConnectionMultiplexer.Connect(redis.Options);
        services.AddSingleton(new CacheRedisConnection(multiplexer));
        services.AddSingleton(options);
        services.TryAddSingleton<IInertiaCacheStore, RedisInertiaCacheStore>();
        services.AddHealthChecks().AddCheck<CacheRedisHealthCheck>("cache-redis", tags: ["cache"]);
        return services;
    }

    /// <summary>MainApp invalidates through IInertiaCache; it also owns the renderer.</summary>
    public static IServiceCollection AddInertiaPropInvalidator(this IServiceCollection services)
    {
        services.TryAddSingleton<IPropCacheInvalidator, InertiaPropCacheInvalidator>();
        return services;
    }

    /// <summary>Worker invalidates the shared store directly; without a cache URL it has nothing to invalidate.</summary>
    public static IServiceCollection AddWorkerPropInvalidator(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IInertiaCacheStore)))
            services.TryAddSingleton<IPropCacheInvalidator, RedisPropCacheInvalidator>();
        else
            services.TryAddSingleton<IPropCacheInvalidator, NoOpPropCacheInvalidator>();
        return services;
    }
}
