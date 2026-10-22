using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Features;

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
        if (args.Contains("--create-admin", StringComparer.Ordinal))
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
            var result = await scope.ServiceProvider.GetRequiredService<ManagementService>()
                .CreateAdminAsync(new TeamInput(name, email, password.ToString()), CancellationToken.None);
            if (result.Errors is not null) { Console.Error.WriteLine(string.Join(" ", result.Errors.Values)); Environment.ExitCode = 1; }
            else Console.WriteLine(result.FlashMessage);
            return true;
        }
        if (!args.Contains("--seed-demo", StringComparer.Ordinal)) return false;
        await DemoSeeder.RunAsync(db);
        Console.WriteLine("Demo data seeded. Start the application to process background work.");
        return true;
    }
}
