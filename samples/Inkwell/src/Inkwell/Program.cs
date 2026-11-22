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

// Load the nearest .env before ASP.NET reads environment configuration. Deployment variables win.
DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();

builder.AddBlogData();
builder.AddBlogAuth();
builder.AddBlogInertia();
builder.AddBlogFeatures();
builder.AddStoreRunwick();

var app = builder.Build();
if (RunwickSetup.IsEnabled(app.Configuration) && !RunwickManagementAuth.DashboardAllowed(app.Configuration, app.Environment))
    app.Logger.LogWarning("Runwick dashboard is not mapped: set RUNWICK_ADMIN_PASSWORD to a non-default value to enable it.");
else if (RunwickSetup.IsEnabled(app.Configuration))
{
    // Terminal branches: map before Inertia's version check.
    app.MapRunwickDashboard(RunwickSetup.DashboardPath);
    app.MapRunwickManagement(RunwickSetup.ManagementPath);
    app.MapRunwickManagementLogin();
}
app.UseBlogPipeline();
app.MapHub<BlogHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);
// Live props: public post counters stay on the raw hub above; per-user dashboards refresh over this hub.
app.MapInertiaLiveHub();

app.MapHealthChecks("/health");
// The client heartbeat proves the session is alive; unnamed so the route generator skips it.
app.MapGet("/ping", () => Results.NoContent());
app.MapInertiaDevTools(); // Development only: the Inertia DevTools extension reads GET /_inertia/devtools/entries/{id}
app.MapGet("/", () => Results.Redirect("/posts"));
app.MapAuthEndpoints();
app.MapPostEndpoints();
app.MapSocialEndpoints();
Inkwell.Features.Notifications.Notifications.MapNotifications(app);
app.MapUploadEndpoints();
app.MapAnalyticsEndpoints();
app.MapSettingsEndpoints();
Inkwell.Features.Team.TeamFeature.MapTeam(app);
app.MapEmailEndpoints();

var seedDemo = args.Contains("--seed-demo");
var createAdmin = args.Contains("--create-admin");
await Startup.InitializeAsync(app.Services, initializeStorage: !seedDemo && !createAdmin);
if (createAdmin)
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
    var result = await scope.ServiceProvider.GetRequiredService<Inkwell.Features.Team.TeamService>().CreateAsync(new(name, email, password.ToString()));
    if (result.Errors is not null) { Console.Error.WriteLine(string.Join(" ", result.Errors.Values)); Environment.ExitCode = 1; }
    else Console.WriteLine("Administrator created.");
    return;
}
if (seedDemo)
{
    await using var scope = app.Services.CreateAsyncScope();
    await DemoSeeder.RunAsync(scope.ServiceProvider.GetRequiredService<AppDb>(), CancellationToken.None);
    var built = await scope.ServiceProvider.GetRequiredService<DiscoveryService>().RefreshDirtyProfilesAsync();
    Console.WriteLine($"Built {built} interest profiles.");
    return;
}

if (RunwickSetup.DemoEnabled(app.Configuration, app.Environment)) app.MapRunwickDemoJobs();
app.Run();

public partial class Program;
