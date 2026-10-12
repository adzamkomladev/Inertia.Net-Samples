using TickerQ.DependencyInjection;
using ReactSqliteReplicasBlog.Features.Analytics;
using ReactSqliteReplicasBlog.Features.Auth;
using ReactSqliteReplicasBlog.Features.Posts;
using ReactSqliteReplicasBlog.Features.Settings;
using ReactSqliteReplicasBlog.Features.Social;
using ReactSqliteReplicasBlog.Features.Uploads;
using ReactSqliteReplicasBlog.Infrastructure;
using ReactSqliteReplicasBlog.Infrastructure.Setup;

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
app.MapGet("/", () => Results.Redirect("/posts"));
app.MapAuthEndpoints();
app.MapPostEndpoints();
app.MapSocialEndpoints();
DigestService.MapUnsubscribe(app);
app.MapUploadEndpoints();
app.MapAnalyticsEndpoints();
app.MapSettingsEndpoints();

await Startup.InitializeAsync(app.Services, initializeStorage: !Cli.HasFlag(args, "--migrate") && !Cli.HasFlag(args, "--seed-demo") && !Cli.HasFlag(args, "--reindex-search"));
if (await Cli.TryRunAsync(app, args)) return;

if (app.Configuration.GetValue("Cron:Enabled", true)) app.UseTickerQ();
app.Run();

public partial class Program;
