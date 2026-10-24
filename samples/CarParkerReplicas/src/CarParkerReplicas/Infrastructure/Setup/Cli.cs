using CarParker.Data;
using CarParker.Features;

namespace CarParker.Infrastructure.Setup;

public static class Cli
{
    /// <summary>
    /// Index creation runs on startup by default for a single app. A replica deployment runs the
    /// <c>--initialize</c> one-shot and sets <c>Database:InitializeOnStartup=false</c> on every app
    /// process. The seed and create-admin commands always initialize as well.
    /// </summary>
    public static bool ShouldInitialize(IConfiguration configuration, string[] args) =>
        args.Contains("--initialize", StringComparer.Ordinal)
        || args.Contains("--seed-demo", StringComparer.Ordinal)
        || args.Contains("--create-admin", StringComparer.Ordinal)
        || configuration.GetValue("Database:InitializeOnStartup", true);

    /// <summary>Creates indexes, then runs <c>--seed-demo</c> when asked. Returns true when the process should exit instead of serving.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment() && app.Configuration.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://carparker.example.com).");

        var initializeOnly = args.Contains("--initialize", StringComparer.Ordinal);
        var db = app.Services.GetRequiredService<AppDb>();
        if (ShouldInitialize(app.Configuration, args)) await db.InitializeAsync();
        if (initializeOnly) return true;

        if (args.Contains("--create-admin", StringComparer.Ordinal))
        {
            await using var scope = app.Services.CreateAsyncScope();
            Console.Write("Name: "); var name = Console.ReadLine();
            Console.Write("Email: "); var email = Console.ReadLine();
            Console.Write("Password: ");
            var password = new System.Text.StringBuilder();
            if (Console.IsInputRedirected) password.Append(Console.ReadLine());
            else while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
                else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
            }
            Console.WriteLine();
            var result = await scope.ServiceProvider.GetRequiredService<TeamService>().CreateAsync(new TeamInput(name, email, password.ToString(), "admin"), CancellationToken.None);
            if (result.Errors is not null) { Console.Error.WriteLine(string.Join(" ", result.Errors.Values)); Environment.ExitCode = 1; }
            else Console.WriteLine("Administrator created.");
            return true;
        }
        if (!args.Contains("--seed-demo", StringComparer.Ordinal)) return false;

        await DemoSeeder.RunAsync(db);
        // The admin analytics read daily rollups, which the cron only builds for yesterday and today.
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CarParker.Features.Analytics.LotAnalyticsService>().RollupAsync(CancellationToken.None, days: 30);
        Console.WriteLine("Demo data seeded (resumable via seed_checkpoints).");
        return true;
    }
}
