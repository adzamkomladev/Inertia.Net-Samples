using TickerQ.DependencyInjection;
using SvelteMongoBlog.Features;
using SvelteMongoBlog.Features.Admin;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Lots;
using SvelteMongoBlog.Infrastructure;
using SvelteMongoBlog.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddBlogData().AddBlogAuth().AddBlogInertia().AddBlogFeatures();

var app = builder.Build();
await app.VerifyMongoTransactionsAsync();
if (await Cli.TryRunAsync(app, args)) return;
app.UseBlogPipeline();
app.MapHub<ParkHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);

app.Map("/error", () => Render("Error", new InertiaProps { ["status"] = 500 }).WithStatusCode(500)); // any method: the exception handler re-executes the original request
app.MapHealthChecks("/health");
app.MapInertiaDevTools(); // The Inertia DevTools extension reads GET /_inertia/devtools/entries/{id} (Development only)

app.MapAuth();
app.MapLots();       // also owns GET / (home)
app.MapBookings();   // customer /account area
app.MapGuard();
app.MapAdmin();
app.MapPreferences();
app.MapUploads();

if (app.Configuration.GetValue("Cron:Enabled", true)) app.UseTickerQ();
app.Run();

public partial class Program;
