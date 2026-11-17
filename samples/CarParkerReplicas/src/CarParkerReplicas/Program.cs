using CarParker.Features;
using CarParker.Features.Admin;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Lots;
using CarParker.Infrastructure;
using CarParker.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
QuestPDF.Settings.UseSystemFonts = false; // Lato ships inside QuestPDF: identical output everywhere, no fontconfig in the image
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
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
app.MapGet("/alive", (IConfiguration config, IHostEnvironment env) => env.IsDevelopment()
    ? Results.Ok(new { status = "alive", instance = ReplicaIdentity.FromConfiguration(config) }) // hostname: Development only
    : Results.Ok(new { status = "alive" }));
app.MapInertiaDevTools(); // The Inertia DevTools extension reads GET /_inertia/devtools/entries/{id} (Development only)

app.MapAuth();
app.MapLots();       // also owns GET / (home)
app.MapBookings();   // customer /account area
app.MapBookingDocuments(); // PDF receipts
app.MapEmailPreviews();    // Development-only /dev/emails
app.MapGuard();
app.MapAdmin();
app.MapPreferences();
app.MapUploads();
if (RunwickSetup.DemoEnabled(app.Configuration, app.Environment)) app.MapRunwickDemoJobs();

CronSetup.WarmScheduleEvaluation(app.Configuration); // see the method: keeps Runwick's startup schedule budget from tripping on a cold start
app.Run();

public partial class Program;
