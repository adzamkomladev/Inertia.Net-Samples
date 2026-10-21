using TickerQ.DependencyInjection;
using ReactSqliteBlog.Features.Analytics;
using ReactSqliteBlog.Features.Auth;
using ReactSqliteBlog.Features.Posts;
using ReactSqliteBlog.Features.Settings;
using ReactSqliteBlog.Features.Social;
using ReactSqliteBlog.Features.Uploads;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Infrastructure.Setup;

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
app.MapInertiaDevTools(); // The Inertia DevTools extension reads GET /_inertia/devtools/entries/{id} (Development only)
app.MapGet("/", () => Results.Redirect("/posts"));
app.MapAuthEndpoints();
app.MapPostEndpoints();
app.MapSocialEndpoints();
DigestService.MapUnsubscribe(app);
app.MapUploadEndpoints();
app.MapAnalyticsEndpoints();
app.MapSettingsEndpoints();

await Startup.InitializeAsync(app.Services, initializeStorage: !Cli.HasFlag(args, "--seed-demo") && !Cli.HasFlag(args, "--reindex-search"));
if (await Cli.TryRunAsync(app, args)) return;

if (app.Configuration.GetValue("Cron:Enabled", true)) app.UseTickerQ();
app.Run();

public partial class Program;
