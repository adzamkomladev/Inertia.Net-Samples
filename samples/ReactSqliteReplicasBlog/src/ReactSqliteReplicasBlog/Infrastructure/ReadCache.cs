using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ReactSqliteReplicasBlog.Infrastructure;

/// <summary>Namespaced best-effort cache for reconstructible reads; a cache outage always falls back to the database.</summary>
public sealed class OptionalReadCache(IDistributedCache? cache, OptionalServiceNamespace serviceNamespace)
{
    static readonly DistributedCacheEntryOptions EntryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (cache is null) return null;
        try
        {
            var value = await cache.GetStringAsync(serviceNamespace.RedisKey(key), cancellationToken);
            return value is null ? null : JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { return null; }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (cache is null) return;
        try
        {
            await cache.SetStringAsync(serviceNamespace.RedisKey(key), JsonSerializer.Serialize(value, JsonOptions), EntryOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        if (cache is null) return await factory();
        var namespaced = serviceNamespace.RedisKey(key);
        try
        {
            var value = await cache.GetStringAsync(namespaced, cancellationToken);
            if (value is not null)
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<T>(value, JsonOptions);
                    if (cached is not null) return cached;
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { }

        var result = await factory();
        try
        {
            var serialized = JsonSerializer.Serialize(result, JsonOptions);
            await cache.SetStringAsync(namespaced, serialized, EntryOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { }
        return result;
    }
}
