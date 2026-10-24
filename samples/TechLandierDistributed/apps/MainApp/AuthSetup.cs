using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using StackExchange.Redis;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddStoreAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var secureCookies = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.LogoutPath = "/logout";
                o.AccessDeniedPath = "/email/verify";
                // Scoped away from the original single-process TechLandier on localhost.
                o.Cookie.Name = ".tld.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = AuthHttp.ValidatePrincipal;
                // Inertia visits should follow a redirect to the login/verify page instead of getting a bare 401/403.
                var redirectToLogin = o.Events.OnRedirectToLogin;
                var redirectToAccessDenied = o.Events.OnRedirectToAccessDenied;
                o.Events.OnRedirectToLogin = async ctx =>
                {
                    if (ctx.Request.Headers.ContainsKey("X-Inertia")) ctx.Response.Redirect(ctx.RedirectUri);
                    else await redirectToLogin(ctx);
                };
                o.Events.OnRedirectToAccessDenied = async ctx =>
                {
                    if (ctx.Request.Headers.ContainsKey("X-Inertia")) ctx.Response.Redirect(ctx.RedirectUri);
                    else await redirectToAccessDenied(ctx);
                };
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("verified", p => p.RequireAuthenticatedUser().RequireClaim(AuthMappers.VerifiedClaim, "true"))
            .AddPolicy("admin", p => p.RequireAuthenticatedUser().RequireClaim(ClaimTypes.Role, "admin"));

        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = ".tld.af";
            o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                // Inertia clients need a redirect with a message, not a bare 429.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                    await Back().WithFlash("toast", new Toast("error", "Too many attempts. Please wait a minute and try again.")).ExecuteAsync(ctx.HttpContext);
            };

            var replicas = ReplicaSetup.Enabled(config);
            IConnectionMultiplexer? redis = replicas
                ? (builder.Services.LastOrDefault(d => d.ServiceType == typeof(CriticalRedisConnection))?.ImplementationInstance as CriticalRedisConnection)?.Multiplexer
                : null;
            if (replicas && redis is null) throw new InvalidOperationException("Critical Redis was not registered before rate limiting.");
            var prefix = config["Replicas:Namespace"] ?? "TechLandierDistributed";

            o.AddPolicy("auth", ctx => Partition("auth",
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 10, redis, prefix));
            // Registration only (it is the endpoint with WithInertiaPrecognition, which answers "Precognition: true" without
            // running the handler). Those blur-time field checks get a roomier window of their own so they never spend the
            // real sign-up attempts; any other header value is a real submit and takes the strict window.
            o.AddPolicy("register", ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? Partition("register:precognition", ip, 60, redis, prefix)
                    : Partition("register", ip, 10, redis, prefix);
            });
            o.AddPolicy("uploads", ctx => Partition("uploads",
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 30, redis, prefix));
            o.AddPolicy("views", ctx => Partition("views",
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 60, redis, prefix));
            o.AddPolicy("writes", ctx => Partition("writes",
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 120, redis, prefix));
        });
        return builder;
    }

    static RateLimitPartition<string> Partition(string policy, string identity, int permitLimit, IConnectionMultiplexer? redis, string prefix) =>
        redis is null
            ? RateLimitPartition.GetFixedWindowLimiter(policy + ":" + identity,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.Get(policy + ":" + identity,
                key => new RedisFixedWindowRateLimiter(redis, $"{prefix}:limits:{policy}:{identity}", permitLimit));
}
