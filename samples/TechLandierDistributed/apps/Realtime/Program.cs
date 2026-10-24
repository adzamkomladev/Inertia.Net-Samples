using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;
using TechLandier.Realtime;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddCoreData();
builder.AddRealtimeAuth();
// Publishes and accepts hub messages through the same Redis channel as the worker.
builder.Services.AddCoreRealtimeBackplane(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDb>();

var app = builder.Build();
app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));
app.UseMiddleware<RealtimeOriginMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Realtime-Replica"] = app.Configuration["INSTANCE_ID"] ?? Environment.MachineName;
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<StoreHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
await app.RunAsync();

public partial class Program;

/// <summary>Stable entry-point marker for the integration tests.</summary>
public sealed class RealtimeHostMarker;
