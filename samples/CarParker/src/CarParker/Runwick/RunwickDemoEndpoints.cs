using Runwick;

namespace CarParker.Infrastructure.Setup;

/// <summary>
/// Open, unauthenticated endpoints for exercising the Runwick worker pools. They only dispatch
/// side-effect-free sleep jobs, so they are safe to leave enabled in a sample.
/// </summary>
public static class RunwickDemoEndpoints
{
    private sealed record DemoJobKind(string Key, TimeSpan Duration, string JobName, string Queue, string Pool, string Priority);

    private static readonly DemoJobKind[] Kinds =
    [
        new("100ms", TimeSpan.FromMilliseconds(100), "demo.sleep.100ms", "demo-fast", "fixed 4", "high"),
        new("500ms", TimeSpan.FromMilliseconds(500), "demo.sleep.500ms", "demo-scaled", "auto 1-8", "medium"),
        new("2s", TimeSpan.FromSeconds(2), "demo.sleep.2s", "demo-scaled", "auto 1-8", "medium"),
        new("10s", TimeSpan.FromSeconds(10), "demo.sleep.10s", "demo-slow", "auto 1-4", "medium"),
        new("30s", TimeSpan.FromSeconds(30), "demo.sleep.30s", "demo-long", "auto 1-4", "low"),
    ];

    public static IEndpointRouteBuilder MapRunwickDemoJobs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/demo/jobs").DisableAntiforgery();

        group.MapGet("", (HttpRequest request) => Results.Json(new
        {
            runner = "Runwick",
            dashboard = $"{request.Scheme}://{request.Host}{RunwickSetup.DashboardPath}",
            login = $"{request.Scheme}://{request.Host}{RunwickManagementAuth.LoginPath}",
            jobs = Kinds.Select(k => new
            {
                job = k.Key,
                k.JobName,
                durationMs = (int)k.Duration.TotalMilliseconds,
                k.Queue,
                k.Pool,
                k.Priority,
                single = $"POST {request.Scheme}://{request.Host}/demo/jobs/{k.Key}",
                batch = $"POST {request.Scheme}://{request.Host}/demo/jobs/{k.Key}/batch?count=100",
            }),
        }));

        group.MapPost("/{kind}", async (string kind, IJobDispatcher dispatcher, HttpRequest request, CancellationToken ct) =>
        {
            if (Find(kind) is not { } selected) return Unknown(kind);

            var batchId = Guid.NewGuid();
            var receipt = await DispatchOneAsync(dispatcher, selected, batchId, 0, ct);
            return Results.Json(new
            {
                runner = "Runwick",
                job = selected.Key,
                selected.JobName,
                selected.Queue,
                selected.Pool,
                batchId,
                jobId = receipt.JobId.ToString(),
                operationId = receipt.OperationId.ToString(),
                dashboard = $"{request.Scheme}://{request.Host}{RunwickSetup.DashboardPath}",
            }, statusCode: StatusCodes.Status202Accepted);
        });

        group.MapPost("/{kind}/batch", async (string kind, int? count, IJobDispatcher dispatcher, HttpRequest request, CancellationToken ct) =>
        {
            if (Find(kind) is not { } selected) return Unknown(kind);
            var size = count ?? 100;
            if (size is < 1 or > 100)
                return Results.BadRequest(new { error = "count must be between 1 and 100." });

            var batchId = Guid.NewGuid();
            var enqueuedAt = DateTimeOffset.UtcNow;
            var items = Enumerable.Range(0, size)
                .Select(index => new BatchDispatchItem(Create(selected, batchId, index, enqueuedAt), Options(selected, batchId, index)))
                .ToList();
            var receipt = await dispatcher.DispatchManyAsync(items, new BatchDispatchOptions { ContinueOnConflict = false }, ct);
            return Results.Json(new
            {
                runner = "Runwick",
                job = selected.Key,
                selected.JobName,
                selected.Queue,
                selected.Pool,
                batchId,
                requested = size,
                accepted = receipt.Items.Count(i => i.Receipt is not null),
                operationId = receipt.OperationId.ToString(),
                jobs = receipt.Items.Select(i => new
                {
                    ordinal = i.Ordinal,
                    jobId = i.Receipt?.JobId.ToString(),
                    error = i.Failure?.Code,
                    message = i.Failure?.Message,
                }),
                dashboard = $"{request.Scheme}://{request.Host}{RunwickSetup.DashboardPath}",
            }, statusCode: StatusCodes.Status202Accepted);
        });

        return app;
    }

    private static DemoJobKind? Find(string kind) =>
        Kinds.FirstOrDefault(k => string.Equals(k.Key, kind, StringComparison.OrdinalIgnoreCase));

    private static IResult Unknown(string kind) =>
        Results.BadRequest(new { error = $"Unknown demo job '{kind}'.", jobs = Kinds.Select(k => k.Key) });

    private static async Task<DispatchReceipt> DispatchOneAsync(
        IJobDispatcher dispatcher, DemoJobKind kind, Guid batchId, int index, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return kind.Key switch
        {
            "100ms" => await dispatcher.DispatchAsync(new DemoSleep100ms(batchId, index, now), Options(kind, batchId, index), ct),
            "500ms" => await dispatcher.DispatchAsync(new DemoSleep500ms(batchId, index, now), Options(kind, batchId, index), ct),
            "2s" => await dispatcher.DispatchAsync(new DemoSleep2s(batchId, index, now), Options(kind, batchId, index), ct),
            "10s" => await dispatcher.DispatchAsync(new DemoSleep10s(batchId, index, now), Options(kind, batchId, index), ct),
            "30s" => await dispatcher.DispatchAsync(new DemoSleep30s(batchId, index, now), Options(kind, batchId, index), ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind.Key, "Unknown demo job."),
        };
    }

    private static IQueueJob Create(DemoJobKind kind, Guid batchId, int index, DateTimeOffset enqueuedAt) => kind.Key switch
    {
        "100ms" => new DemoSleep100ms(batchId, index, enqueuedAt),
        "500ms" => new DemoSleep500ms(batchId, index, enqueuedAt),
        "2s" => new DemoSleep2s(batchId, index, enqueuedAt),
        "10s" => new DemoSleep10s(batchId, index, enqueuedAt),
        "30s" => new DemoSleep30s(batchId, index, enqueuedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind.Key, "Unknown demo job."),
    };

    private static DispatchOptions Options(DemoJobKind kind, Guid batchId, int index) => new()
    {
        Priority = kind.Priority,
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo.kind"] = kind.Key,
            ["demo.batch"] = batchId.ToString("N"),
            ["demo.index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
    };
}
