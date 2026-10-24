using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using StackExchange.Redis;
using CarParker.Features;

namespace CarParker.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddBlogAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        // Development defaults to SameAsRequest; every non-Development deployment opts into Secure
        // explicitly (or with AUTH_SECURE_COOKIES=true behind a TLS-terminating proxy).
        var secureCookies = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.LogoutPath = "/logout";
                o.AccessDeniedPath = "/email/verify";
                o.Cookie.Name = ".cpr.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = AuthHttp.ValidatePrincipal;
                // The cookie handler answers XHR (X-Requested-With) with a bare 401/403; Inertia visits should follow a redirect to the login/verify page instead.
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
            .AddPolicy("verified", p => p.RequireAuthenticatedUser().RequireClaim(AuthHelpers.VerifiedClaim, "true"))
            .AddPolicy("admin", p => p.RequireAuthenticatedUser().RequireClaim(AuthHelpers.RoleClaim, "admin"))
            .AddPolicy("guard", p => p.RequireAuthenticatedUser().RequireClaim(AuthHelpers.RoleClaim, "guard", "admin"));

        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = ".cpr.af";
            o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                // Inertia clients expect a redirect with something to show, not a bare 429.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                    await Back().WithFlash("toast", new Toast("error", "Too many attempts. Please wait a minute and try again.")).ExecuteAsync(ctx.HttpContext);
            };
            o.AddPolicy("auth", ctx => Partition(ctx, "auth", 10, byUser: false));
            // Registration only (it is the endpoint with WithInertiaPrecognition, which answers "Precognition: true" without
            // running the handler). Those blur-time field checks get a roomier window of their own so they never spend the
            // real sign-up attempts; any other header value is a real submit and takes the strict window.
            o.AddPolicy("register", ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? Partition(ctx, "register:precognition", 60, partitionIdentity: $"precognition:{ip}")
                    : Partition(ctx, "register", 10, partitionIdentity: ip);
            });
            o.AddPolicy("uploads", ctx => Partition(ctx, "uploads", 30));
            o.AddPolicy("views", ctx => Partition(ctx, "views", 60));
            o.AddPolicy("writes", ctx => Partition(ctx, "writes", 120));
        });
        return builder;

        RateLimitPartition<string> Partition(HttpContext ctx, string policy, int limit,
            bool byUser = true, string? partitionIdentity = null)
        {
            var identity = partitionIdentity
                ?? (byUser ? ctx.User.UserIdOrNull()?.ToString() : null)
                ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{ReplicaSetup.Namespace(config, builder.Environment.EnvironmentName)}:limits:{policy}:{identity}";
            return ReplicaSetup.Enabled(config) && ReplicaSetup.HasRedis(config)
                ? RateLimitPartition.Get(key, _ => new RedisFixedWindowRateLimiter(
                    ctx.RequestServices.GetRequiredService<IConnectionMultiplexer>(), key, limit))
                : RateLimitPartition.GetFixedWindowLimiter(key,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
        }
    }
}
