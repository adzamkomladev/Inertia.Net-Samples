using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.Infrastructure.Setup;
using VuePgReplicasAdvBlog.Realtime;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddServiceDefaults();
builder.AddCommonData().AddRealtimeAuth().AddReplicaServices();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDb>();

var app = builder.Build();
app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));
app.UseMiddleware<RealtimeOriginMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Realtime-Replica"] = app.Configuration["INSTANCE_ID"] ?? Environment.MachineName;
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<BlogHub>("/realtime", options => options.CloseOnAuthenticationExpiration = true);
app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
await app.RunAsync();

namespace VuePgReplicasAdvBlog.Realtime
{
    public sealed class RealtimeHost;
}
