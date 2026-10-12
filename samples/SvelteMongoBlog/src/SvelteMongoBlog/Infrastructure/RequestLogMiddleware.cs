using System.Diagnostics;

namespace SvelteMongoBlog.Infrastructure;

/// One line per request: method, path (no query), status, duration, memory, prefetch flag, trace id. Level: Logging:LogLevel:SvelteMongoBlog.Infrastructure.RequestLogMiddleware.
/// Static bundle and icon requests are skipped so they do not drown the log.
public sealed class RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> log)
{
    static bool IsStaticAsset(PathString path) => path.StartsWithSegments("/build") || path == "/favicon.svg";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (IsStaticAsset(ctx.Request.Path))
        {
            await next(ctx);
            return;
        }
        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try { await next(ctx); }
        catch { failed = true; throw; }
        finally
        {
            var h = ctx.Request.Headers;
            var prefetch = h.ContainsKey("X-Inertia-Prefetch") || h["Purpose"] == "prefetch" || h["Sec-Purpose"].ToString().Contains("prefetch");
            using var process = Process.GetCurrentProcess();
            log.LogInformation("HTTP {Method} {Path} => {Status} in {Ms:F1} ms | Process {WorkingSetMiB:F1} MiB | Heap {HeapMiB:F1} MiB | Prefetch {Prefetch} | Trace {TraceId}",
                ctx.Request.Method, ctx.Request.Path.Value, failed ? 500 : ctx.Response.StatusCode,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds, process.WorkingSet64 / 1048576d, GC.GetTotalMemory(false) / 1048576d, prefetch, ctx.TraceIdentifier);
        }
    }
}
