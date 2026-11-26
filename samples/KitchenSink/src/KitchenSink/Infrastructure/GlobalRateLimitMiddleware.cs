using System.Collections.Concurrent;

namespace KitchenSink.Infrastructure;

/// <summary>
/// The demo's global per-address limiter: 120 page requests a minute, with a higher bucket for partial
/// reloads so polling and infinite scroll do not exhaust it. Disabled in the ordinary test host.
/// </summary>
public sealed class GlobalRateLimitMiddleware(RequestDelegate next, IConfiguration config)
{
    readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!config.GetValue("Demo:GlobalRateLimit", true)
            || context.Request.Path.StartsWithSegments("/build")
            || context.Request.Path.StartsWithSegments("/inertia-live")
            || context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var partial = context.Request.Headers.ContainsKey("X-Inertia-Partial-Component");
        var limit = partial
            ? config.GetValue("Demo:PartialRateLimit", 600)
            : config.GetValue("Demo:GlobalRateLimitPerMinute", 120);
        var key = (partial ? "partial|" : "full|") + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
        var now = DateTimeOffset.UtcNow;

        int count;
        lock (bucket)
        {
            if (now - bucket.Start >= TimeSpan.FromMinutes(1))
            {
                bucket.Start = now;
                bucket.Count = 0;
            }

            count = ++bucket.Count;
        }

        if (count > limit)
        {
            context.Response.Headers.RetryAfter = "60";
            await Inertia.Net.Inertia.Render("ErrorPage", new { status = StatusCodes.Status429TooManyRequests })
                .WithStatusCode(StatusCodes.Status429TooManyRequests)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    sealed class Bucket
    {
        public DateTimeOffset Start { get; set; } = DateTimeOffset.UtcNow;
        public int Count { get; set; }
    }
}
