using KitchenSink.Data;
using KitchenSink.Features;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Infrastructure.Setup;

public static class Cli
{
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        if (!args.Contains("--seed-demo") && !args.Contains("--reset-demo"))
        {
            return false;
        }

        await app.InitializeKitchenSinkDataAsync();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();

        if (args.Contains("--reset-demo"))
        {
            var resetAt = await DemoSeeder.ResetAsync(db);
            await scope.ServiceProvider.GetRequiredService<IInertiaLive>().PublishAsync(DemoEvents.Reset);
            Console.WriteLine($"Demo data reset at {resetAt:O}.");
            return true;
        }

        Console.WriteLine($"Demo data ready: {await db.Contacts.CountAsync()} contacts, {await db.Organizations.CountAsync()} organizations.");
        Console.WriteLine($"Login: {DemoSeeder.DemoEmail} / {DemoSeeder.DemoPassword}");
        return true;
    }
}
