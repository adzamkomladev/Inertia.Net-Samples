using KitchenSink.Data;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features;

/// <summary>
/// Replaces the CRM and live-prop data once a day, like the upstream demo's scheduled demo:reset command.
/// Runs in-process so the DemoDataReset broadcast reaches the pages connected to this instance.
/// </summary>
public sealed class DemoResetScheduler(
    IServiceScopeFactory scopes,
    IInertiaLive live,
    IConfiguration config,
    ILogger<DemoResetScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Demo:DailyReset", true))
        {
            return;
        }

        var hour = Math.Clamp(config.GetValue("Demo:DailyResetHourUtc", 3), 0, 23);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
                if (next <= now)
                {
                    next = next.AddDays(1);
                }

                await Task.Delay(next - now, stoppingToken);

                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var resetAt = await DemoSeeder.ResetAsync(db, stoppingToken);
                await live.PublishAsync(DemoEvents.Reset, cancellationToken: stoppingToken);
                logger.LogInformation("Daily demo reset completed at {ResetAt:O}.", resetAt);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
