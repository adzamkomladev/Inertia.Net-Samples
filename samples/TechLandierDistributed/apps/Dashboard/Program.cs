using System.Security.Claims;
using Runwick;
using Runwick.Management;
using TechLandier.Dashboard;
using TechLandier.Infrastructure;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ApplyPlainNames();

var config = builder.Configuration;
var credentials = RunwickManagementAuth.Credentials(config);
RunwickManagementAuth.EnsureCredentialsAllowed(credentials, builder.Environment);
builder.AddRunwickManagementAuth(credentials);
// Sign-in attempts per client address; the dashboard has no other public mutation.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddRunwick(runwick =>
{
    runwick.Application(RunwickDefaults.ApplicationName)
        .UsePostgres(RunwickDefaults.ConnectionString(config))
        .AddRunwickJobs(RunwickDefaults.DemoEnabled(config, builder.Environment));
});

var grants = new List<ManagementGrant>(ManagementRoles.Administrator)
{
    new(ManagementPermissions.JobsReadPayload),
    new(ManagementPermissions.JobsReadError),
    new(ManagementPermissions.JobsDispatch),
    new(ManagementPermissions.AuditRead),
};
builder.Services.AddRunwickManagement(management => management
    .AuthenticationSchemes(RunwickManagementAuth.Scheme)
    .MapGrants(principal => IsDashboardAdmin(principal, credentials.Username) ? grants : []));
builder.Services.AddRunwickDashboard("default", dashboard =>
{
    dashboard.Title = "TechLandier Jobs";
    dashboard.SignOutPath = RunwickManagementAuth.LogoutPath;
});

var app = builder.Build();
// HAProxy terminates TLS; trusted forwarded headers give the real scheme, host and client IP, so the logout Origin
// check and the per-IP sign-in limit see what the browser saw.
app.UseForwardedHeaders(ProxySetup.TrustedProxyOptions(app.Configuration));
app.UseRateLimiter();
app.MapRunwickDashboard("/runwick");
app.MapRunwickManagement("/runwick-api");
app.MapRunwickManagementLogin();
app.MapGet("/health", () => Results.Ok(new { status = "ok", role = "dashboard" }));
app.MapGet("/", () => Results.Redirect("/runwick"));
await app.RunAsync();

static bool IsDashboardAdmin(ClaimsPrincipal principal, string username) =>
    principal.Identity?.IsAuthenticated == true
    && string.Equals(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, username, StringComparison.Ordinal);

public partial class DashboardHostMarker;
