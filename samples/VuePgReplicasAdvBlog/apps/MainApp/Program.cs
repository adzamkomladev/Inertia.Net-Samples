using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.Infrastructure.Setup;
using VuePgReplicasAdvBlog.MainApp.Endpoints;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames(
    ("SSR_ENABLED", "Inertia:Ssr"),
    ("SSR_EXECUTABLE", "Inertia:SsrExecutable"),
    ("SSR_TIMEOUT_SECONDS", "Inertia:SsrTimeoutSeconds"));
builder.AddServiceDefaults();
builder.AddCommonData().AddCommonInfrastructure().AddMainAppAuth().AddMainAppInertia().AddMainAppServices();

var app = builder.Build();
if (await Cli.TryRunAsync(app, args)) return;
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Replica"] = app.Configuration["INSTANCE_ID"] ?? Environment.MachineName;
    await next();
});
app.UseMainAppPipeline();

app.MapGet("/", () => Results.Redirect("/posts"));
app.MapGet("/error", () => Render("Error", new InertiaProps { ["status"] = 500 }).WithStatusCode(500));
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });

var endpoints = app.MapGroup("").AddEndpointFilter(new FeatureErrorFilter());
endpoints.MapAuth();
endpoints.MapPosts();
endpoints.MapComments();
endpoints.MapLikes();
endpoints.MapSocial();
endpoints.MapDigests();
endpoints.MapPreferences();
endpoints.MapAnalytics();
endpoints.MapUploads();

await app.RunAsync();

public partial class Program;
