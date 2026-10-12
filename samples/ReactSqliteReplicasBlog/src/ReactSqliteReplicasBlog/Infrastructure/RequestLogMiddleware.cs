using System.Diagnostics;

namespace ReactSqliteReplicasBlog.Infrastructure;

public sealed class RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        Exception? failure = null;
        try { await next(context); }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            using var process = Process.GetCurrentProcess();
            logger.LogInformation(
                "HTTP {Method} {Path} => {StatusCode} in {ElapsedMs:F1} ms | Process {WorkingSetMiB:F1} MiB | Managed heap {ManagedHeapMiB:F1} MiB | Prefetch {Prefetch} | Trace {TraceId}",
                context.Request.Method, context.Request.Path.Value,
                failure is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                process.WorkingSet64 / 1048576d, GC.GetTotalMemory(false) / 1048576d,
                context.Request.Headers.ContainsKey("X-Inertia-Prefetch") || context.Request.Headers["Purpose"] == "prefetch",
                context.TraceIdentifier);
        }
    }
}
