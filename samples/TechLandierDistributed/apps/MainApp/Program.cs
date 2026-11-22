using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TechLandier.Features;
using TechLandier.Infrastructure;
using TechLandier.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddCoreData().AddStoreAuth().AddStoreInertia().AddStoreFeatures().AddRunwickProducer();

var app = builder.Build();
if (await Cli.TryRunAsync(app, args)) return;

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Replica"] = app.Configuration["INSTANCE_ID"] ?? Environment.MachineName;
    await next(context);
});
app.UseStorePipeline();
app.MapGet("/error", () => Render("Error", new InertiaProps { ["status"] = 500 }).WithStatusCode(500));
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
// The client heartbeat proves the session is alive; unnamed so the route generator skips it.
app.MapGet("/ping", () => Results.NoContent());
// One-click unsubscribe pages, and (Development only) the email template gallery.
app.MapEmailEndpoints();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
QuestPDF.Settings.UseSystemFonts = false;
app.MapInertiaDevTools();

var features = app.MapGroup("").AddEndpointFilter<FeatureResponses>();
features.MapStore();
features.MapCheckout();
features.MapOrders();
features.MapReviews();
features.MapCredit();
features.MapCare();
features.MapSupport();
features.MapDeliveries();
features.MapAlerts();
features.MapAccount();
features.MapAdmin();
features.MapManagement();
features.MapDocuments();
features.MapAuth();
features.MapUploads();
if (RunwickDefaults.DemoEnabled(app.Configuration, app.Environment)) app.MapRunwickDemoJobs();

await app.RunAsync();

public partial class Program;

/// <summary>Stable entry-point marker for the integration tests.</summary>
public sealed class MainAppHostMarker;
