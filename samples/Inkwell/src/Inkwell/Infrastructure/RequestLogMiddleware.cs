using System.Diagnostics;

// Environment.WorkingSet reads one counter; Process.GetCurrentProcess() allocated a Process snapshot on every request.

namespace Inkwell.Infrastructure;

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
            logger.LogInformation(
                "HTTP {Method} {Path} => {StatusCode} in {ElapsedMs:F1} ms | Process {WorkingSetMiB:F1} MiB | Managed heap {ManagedHeapMiB:F1} MiB | Prefetch {Prefetch} | Trace {TraceId}",
                context.Request.Method, context.Request.Path.Value,
                failure is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                Environment.WorkingSet / 1048576d, GC.GetTotalMemory(false) / 1048576d,
                context.Request.Headers.ContainsKey("X-Inertia-Prefetch") || context.Request.Headers["Purpose"] == "prefetch",
                context.TraceIdentifier);
        }
    }
}
