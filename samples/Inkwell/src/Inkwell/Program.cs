using Inkwell.Data;
using Inkwell.Features.Analytics;
using Inkwell.Features.Auth;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Features.Settings;
using Inkwell.Features.Social;
using Inkwell.Features.Uploads;
using Inkwell.Infrastructure;
using Inkwell.Infrastructure.Setup;
using TickerQ.DependencyInjection;

// Load the nearest .env before ASP.NET reads environment configuration. Deployment variables win.
DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();

builder.AddBlogData();
builder.AddBlogAuth();
builder.AddBlogInertia();
builder.AddBlogFeatures();

var app = builder.Build();
app.UseBlogPipeline();
app.MapHub<BlogHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);

app.MapHealthChecks("/health");
app.MapInertiaDevTools(); // Development only: the Inertia DevTools extension reads GET /_inertia/devtools/entries/{id}
app.MapGet("/", () => Results.Redirect("/posts"));
app.MapAuthEndpoints();
app.MapPostEndpoints();
app.MapSocialEndpoints();
app.MapUploadEndpoints();
app.MapAnalyticsEndpoints();
app.MapSettingsEndpoints();
app.MapGet("/email/unsubscribe/daily", async (string? token, DigestService digest, CancellationToken ct) =>
        await digest.UnsubscribeAsync(token, ct)
            ? Results.Content("<!doctype html><html><body><h1>You're unsubscribed</h1><p>Daily recommendation emails are turned off.</p></body></html>", "text/html")
            : Results.BadRequest("This unsubscribe link is invalid or expired."))
    .AllowAnonymous();

var seedDemo = args.Contains("--seed-demo");
await Startup.InitializeAsync(app.Services, initializeStorage: !seedDemo);
if (seedDemo)
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoSeeder.RunAsync(scope.ServiceProvider.GetRequiredService<AppDb>(), CancellationToken.None);
    var queued = await scope.ServiceProvider.GetRequiredService<DiscoveryService>().RefreshProfilesAsync();
    await scope.ServiceProvider.GetRequiredService<AppDb>().SaveChangesAsync();
    Console.WriteLine($"Queued {queued} initial profile refreshes. Start the application to process background work.");
    return;
}

if (app.Configuration.GetValue("Cron:Enabled", true)) app.UseTickerQ();
app.Run();

public partial class Program;
