using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TechLandier.Features;
using TechLandier.Infrastructure;
using TechLandier.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddCoreData().AddStoreAuth().AddStoreInertia().AddStoreFeatures();

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
features.MapAuth();
features.MapUploads();
PromoUnsubscribe.Map(app);

await app.RunAsync();

public partial class Program;

/// <summary>Stable entry-point marker for the integration tests.</summary>
public sealed class MainAppHostMarker;
