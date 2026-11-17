using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using CarParker.Features;

namespace CarParker.Infrastructure;

/// <summary>
/// The per-request cookie validation reads the signed-in user (and avatar key) on every authenticated request. This keeps that
/// lookup for a short time. Writes that change a tracked claim (verification, name, role, theme, phone, avatar) call
/// <see cref="Invalidate"/>; another replica sees the change within <see cref="Ttl"/> at worst. Missing users are never cached.
/// </summary>
public sealed class PrincipalCache(IMemoryCache cache)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    // Bumped by Invalidate. A load that started before an invalidation does not store its (possibly stale) result,
    // so a revoked role cannot be put back into the cache by a read that was already in flight.
    readonly ConcurrentDictionary<Guid, long> generations = new();

    static string Key(Guid userId) => $"principal:{userId}";

    public async Task<AuthAccount?> GetAsync(Guid userId, Func<CancellationToken, Task<AuthAccount?>> load, CancellationToken ct)
    {
        if (cache.TryGetValue(Key(userId), out AuthAccount? hit)) return hit;
        var before = generations.GetValueOrDefault(userId);
        var account = await load(ct);
        if (account is not null && generations.GetValueOrDefault(userId) == before) cache.Set(Key(userId), account, Ttl);
        return account;
    }

    public void Invalidate(Guid userId)
    {
        generations.AddOrUpdate(userId, 1, (_, g) => g + 1);
        cache.Remove(Key(userId));
    }
}
