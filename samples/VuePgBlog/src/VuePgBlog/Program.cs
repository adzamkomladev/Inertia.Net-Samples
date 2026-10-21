using TickerQ.DependencyInjection;
using VuePgBlog.Features;
using VuePgBlog.Infrastructure;
using VuePgBlog.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddStoreData().AddStoreAuth().AddStoreInertia().AddStoreFeatures();

var app = builder.Build();
if (await Cli.TryRunAsync(app, args)) return;
app.UseStorePipeline();
app.MapHub<StoreHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);

// "/" renders the storefront home directly; the store mapping owns every public route.
app.MapGet("/error", () => Render("Error", new InertiaProps { ["status"] = 500 }).WithStatusCode(500));
app.MapHealthChecks("/health");
app.MapInertiaDevTools(); // The Inertia DevTools extension reads GET /_inertia/devtools/entries/{id} (Development only)

app.MapStore();
app.MapCheckout();
app.MapOrders();
app.MapAccountOrders();
app.MapReviews();
app.MapCredit();
app.MapCare();
app.MapAdminCare();
app.MapSupport();
app.MapDeliveries();
app.MapAlerts();
app.MapAccount();
app.MapAdmin();
app.MapAuth();
app.MapUploads();
PromoUnsubscribe.Map(app);

if (app.Configuration.GetValue("Cron:Enabled", true)) app.UseTickerQ();
app.Run();

public partial class Program;