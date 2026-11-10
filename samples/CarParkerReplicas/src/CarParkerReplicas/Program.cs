using CarParker.Features;
using CarParker.Features.Admin;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Lots;
using CarParker.Infrastructure;
using CarParker.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddBlogData().AddBlogAuth().AddBlogInertia().AddBlogFeatures().AddStoreRunwick();

var app = builder.Build();
app.Logger.LogInformation("{Sample} replica {InstanceId} starting", ReplicaSetup.SampleName,
    ReplicaIdentity.FromConfiguration(app.Configuration));
if (await Cli.TryRunAsync(app, args)) return;
if (RunwickSetup.IsEnabled(app.Configuration))
{
    // Terminal branches: map before Inertia's version check.
    app.MapRunwickDashboard(RunwickSetup.DashboardPath);
    app.MapRunwickManagement(RunwickSetup.ManagementPath);
    app.MapRunwickManagementLogin();
}
app.UseBlogPipeline();
app.MapHub<ParkHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);
// Live props: the guard/admin consoles and notification badges refresh over this hub; its SignalR
// registration shares the Redis backplane above, so events fan out across replicas.
app.MapInertiaLiveHub();

app.Map("/error", () => Render("Error", new InertiaProps { ["status"] = 500 }).WithStatusCode(500)); // any method: the exception handler re-executes the original request
app.MapHealthChecks("/health");
app.MapGet("/alive", (IConfiguration config) =>
    Results.Ok(new { status = "alive", instance = ReplicaIdentity.FromConfiguration(config) }));
app.MapInertiaDevTools(); // The Inertia DevTools extension reads GET /_inertia/devtools/entries/{id} (Development only)

app.MapAuth();
app.MapLots();       // also owns GET / (home)
app.MapBookings();   // customer /account area
app.MapGuard();
app.MapAdmin();
app.MapPreferences();
app.MapUploads();
if (RunwickSetup.IsEnabled(app.Configuration)) app.MapRunwickDemoJobs();

app.Run();

public partial class Program;
