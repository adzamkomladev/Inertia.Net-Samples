using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Infrastructure.Setup;

public static class Cli
{
    /// Migrates the database, and with `--seed-demo` loads the demo data and exits instead of serving.
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment() && app.Configuration.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://techlandier.example.com).");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await db.Database.MigrateAsync();
        if (!args.Contains("--seed-demo", StringComparer.Ordinal)) return false;
        await DemoSeeder.RunAsync(db);
        Console.WriteLine("Demo data seeded. Start the application to process background work.");
        return true;
    }
}
