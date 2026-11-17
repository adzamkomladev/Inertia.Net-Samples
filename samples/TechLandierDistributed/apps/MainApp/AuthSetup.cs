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
                // Inertia visits follow the login/verify redirect like browser navigations (303 after POST/PUT/PATCH/DELETE);
                // fetch calls and Precognition requests keep the handler events configured above.
                o.UseInertiaRedirects();
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("verified", p => p.RequireAuthenticatedUser().RequireClaim(AuthMappers.VerifiedClaim, "true"))
            .AddPolicy("admin", p => p.RequireAuthenticatedUser().RequireClaim(ClaimTypes.Role, "admin"));

        builder.Services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(365);
            o.IncludeSubDomains = true;
        });

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
                // Inertia clients need a redirect with something to show, not a bare 429. A rejected write comes back as an
                // error so forms keep their unsaved state.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                {
                    const string message = "Too many attempts. Please wait a minute and try again.";
                    await Back().WithErrors(new Dictionary<string, string> { ["rateLimit"] = message })
                        .WithFlash("toast", new Toast("error", message)).ExecuteAsync(ctx.HttpContext);
                }
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
            // Mutations only: page loads, partial reloads and live-prop reloads (GET/HEAD) are never counted, so the account
            // and admin route groups can apply this policy wholesale.
            o.AddPolicy("writes", ctx => HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)
                ? RateLimitPartition.GetNoLimiter("reads")
                : Partition("writes",
                    ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 120, redis, prefix));
            // Public guest complaint form: unauthenticated and free text, so a tight window per client address.
            o.AddPolicy("guest-support", ctx => Partition("guest-support",
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", 5, redis, prefix, TimeSpan.FromMinutes(10)));
        });
        return builder;
    }

    static RateLimitPartition<string> Partition(string policy, string identity, int permitLimit, IConnectionMultiplexer? redis, string prefix, TimeSpan? window = null) =>
        redis is null
            ? RateLimitPartition.GetFixedWindowLimiter(policy + ":" + identity,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window ?? TimeSpan.FromMinutes(1) })
            : RateLimitPartition.Get(policy + ":" + identity,
                key => new RedisFixedWindowRateLimiter(redis, $"{prefix}:limits:{policy}:{identity}", permitLimit, window));
}
