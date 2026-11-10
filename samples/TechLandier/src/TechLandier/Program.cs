using TechLandier.Features;
using TechLandier.Infrastructure;
using TechLandier.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddStoreData().AddStoreAuth().AddStoreInertia().AddStoreFeatures().AddStoreRunwick();

var app = builder.Build();
if (await Cli.TryRunAsync(app, args)) return;
// The dashboard and its management API are terminal branches: map them before UseInertia so the
// app's Inertia version check never sees their requests.
app.MapRunwickDashboard(RunwickSetup.DashboardPath);
app.MapRunwickManagement(RunwickSetup.ManagementPath);
app.MapRunwickManagementLogin();
app.UseStorePipeline();
app.MapHub<StoreHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);
// Live props: admin dashboards, customer notifications and order timelines refresh over this hub.
app.MapInertiaLiveHub();

app.MapGet("/error", () => Render("Error", new InertiaProps { ["status"] = 500 }).WithStatusCode(500));
app.MapHealthChecks("/health");
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
app.MapRunwickDemoJobs();

app.Run();

public partial class Program;