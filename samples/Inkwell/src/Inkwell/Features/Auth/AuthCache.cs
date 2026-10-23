using Inertia.Net;

namespace Inkwell.Features.Auth;

/// <summary>
/// The shared auth prop is UI data (name, avatar, theme, verification badge, admin menu). It is cached per user
/// for three minutes; profile, preference, verification and role changes invalidate it immediately.
/// </summary>
public static class AuthCache
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan StaleFor = TimeSpan.FromMinutes(1);

    public static string Key(Guid userId) => $"auth:{userId}";
    public static string Tag(Guid userId) => $"auth:{userId}";

    public static async Task InvalidateAsync(IInertiaCache cache, Guid userId, CancellationToken ct) =>
        await cache.RemoveByTagAsync(Tag(userId), ct);
}
