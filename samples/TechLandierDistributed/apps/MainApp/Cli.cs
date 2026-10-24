using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

public static class Cli
{
    /// <summary>
    /// `--migrate` applies migrations and exits; `--seed-demo` seeds after migrating; `--create-admin`
    /// prompts for an administrator. Normal roles only migrate when Database:MigrateOnStartup is true.
    /// The one-shot CLI uses DATABASE_MIGRATE_URL when set so DDL bypasses PgDog.
    /// </summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        var config = app.Configuration;
        var migrate = args.Contains("--migrate", StringComparer.Ordinal);
        var seed = args.Contains("--seed-demo", StringComparer.Ordinal);
        var createAdmin = args.Contains("--create-admin", StringComparer.Ordinal);

        if (!app.Environment.IsDevelopment() && !migrate && config.AppBaseUrl() is null)
            throw new InvalidOperationException("App:BaseUrl must be configured outside Development (set App__BaseUrl to the public URL, e.g. https://techlandier.example.com).");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await using var direct = MigrateOptions(config) is { } options ? new AppDb(options) : null;
        var migrationDb = direct ?? db;
        var shouldMigrate = migrate || seed || createAdmin || config.GetValue("Database:MigrateOnStartup", false);
        if (shouldMigrate)
        {
            await migrationDb.Database.MigrateAsync();
            if (migrate) Console.WriteLine("Database migrations applied.");
        }

        if (createAdmin)
        {
            Console.Write("Name: ");
            var name = Console.ReadLine();
            Console.Write("Email: ");
            var email = Console.ReadLine();
            Console.Write("Password: ");
            var password = new System.Text.StringBuilder();
            if (Console.IsInputRedirected) password.Append(Console.ReadLine());
            else
                while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
                {
                    if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
                    else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
                }
            Console.WriteLine();
            var management = direct is null ? scope.ServiceProvider.GetRequiredService<ManagementService>() : new ManagementService(direct);
            var result = await management.CreateAdminAsync(new TeamInput(name, email, password.ToString()), CancellationToken.None);
            if (result.Errors is not null) { Console.Error.WriteLine(string.Join(" ", result.Errors.Values)); Environment.ExitCode = 1; }
            else Console.WriteLine(result.FlashMessage);
            return true;
        }
        if (seed)
        {
            await DemoSeeder.RunAsync(migrationDb);
            Console.WriteLine("Demo data seeded. Start the application to process background work.");
            return true;
        }
        return migrate;
    }

    static DbContextOptions<AppDb>? MigrateOptions(IConfiguration config)
    {
        var url = config["Database:MigrateUrl"];
        if (string.IsNullOrWhiteSpace(url)) return null;
        return new DbContextOptionsBuilder<AppDb>().UseNpgsql(DatabaseConnection.Parse(url)).Options;
    }
}
