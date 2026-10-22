using CarParker.Data;
using CarParker.Features;

namespace CarParker.Infrastructure.Setup;

public static class Cli
{
    /// <summary>Creates indexes, then runs <c>--seed-demo</c> when asked. Returns true when the process should exit instead of serving.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment() && app.Configuration.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://carparker.example.com).");

        var db = app.Services.GetRequiredService<AppDb>();
        await db.InitializeAsync();
        if (!args.Contains("--seed-demo", StringComparer.Ordinal)) return false;

        await DemoSeeder.RunAsync(db);
        // The admin analytics read daily rollups, which the cron only builds for yesterday and today.
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CarParker.Features.Analytics.LotAnalyticsService>().RollupAsync(CancellationToken.None, days: 30);
        Console.WriteLine("Demo data seeded (resumable via seed_checkpoints).");
        return true;
    }
}
