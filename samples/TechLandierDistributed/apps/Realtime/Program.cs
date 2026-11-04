using System.Text.Json.Serialization.Metadata;
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
// Live props (Inertia.Net 1.6): MainApp pages listen on the per-user, per-order and admins channels;
// this tier hosts the hub, authorized by cookie claims (a user joins its own channel, admins the
// board channel), and the publishes arrive from the worker through the shared backplane.
// Inertia.Net 1.6.3: the live publisher needs the page writer, which AddInertia registers; this tier
// serves no Inertia pages, so no SSR or Vite configuration belongs here.
builder.Services.AddInertia();
builder.Services.AddInertiaLive().AddSignalR(o =>
    o.Authorize(async (context, channel, _) =>
    {
        if (channel.Name == RealtimeNames.AdminChannel)
            return await ValueTask.FromResult(context.User?.IsInRole("admin") == true);
        return await ValueTask.FromResult(context.UserIdentifier is { } id
            && channel.Name == RealtimeNames.UserChannelPrefix + id);
    }));
// The live transport pins its source-generated JSON context into the hub payload resolver chain; keep a
// reflection resolver behind it so the StoreHub's pushed payloads keep serializing under JIT.
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    var chain = o.PayloadSerializerOptions.TypeInfoResolverChain;
    if (!chain.OfType<DefaultJsonTypeInfoResolver>().Any()) chain.Add(new DefaultJsonTypeInfoResolver());
});
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
app.MapInertiaLiveHub();   // /inertia-live; the client negotiates there through the proxy
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
await app.RunAsync();

public partial class Program;

/// <summary>Stable entry-point marker for the integration tests.</summary>
public sealed class RealtimeHostMarker;
