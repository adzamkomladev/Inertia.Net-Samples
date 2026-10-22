using TickerQ.DependencyInjection;
using TechLandier.Features;
using TechLandier.Infrastructure;
using TechLandier.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddStoreData().AddStoreAuth().AddStoreInertia().AddStoreFeatures();

var app = builder.Build();
if (await Cli.TryRunAsync(app, args)) return;
app.UseStorePipeline();
app.MapHub<StoreHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);

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

if (app.Configuration.GetValue("Cron:Enabled", true)) app.UseTickerQ();
app.Run();

public partial class Program;