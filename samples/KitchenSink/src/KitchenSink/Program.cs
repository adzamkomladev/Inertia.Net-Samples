using KitchenSink.Features;
using KitchenSink.Features.Auth;
using KitchenSink.Features.Crm;
using KitchenSink.Features.Showcase;
using KitchenSink.Infrastructure.Setup;

DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
builder.AddKitchenSinkData().AddKitchenSinkAuth().AddKitchenSinkInertia();
builder.Services.AddSingleton<LiveTickBurstService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveTickBurstService>());
builder.Services.AddHostedService<DemoResetScheduler>();

var app = builder.Build();

await app.InitializeKitchenSinkDataAsync();
if (await Cli.TryRunAsync(app, args))
{
    return;
}

app.UseKitchenSinkPipeline();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/error", () => Inertia.Net.Inertia.Render("ErrorPage", new { status = 500 })
    .WithStatusCode(StatusCodes.Status500InternalServerError)).AllowAnonymous();
app.MapInertiaLiveHub();
app.MapInertiaDevTools();

app.MapAuth();
app.MapCrm();
app.MapForms();
app.MapNavigation();
app.MapDataLoading();
app.MapPrefetching();
app.MapState();
app.MapLayouts();
app.MapEvents();
app.MapErrors();
app.MapHttp();

app.Run();

public partial class Program;
