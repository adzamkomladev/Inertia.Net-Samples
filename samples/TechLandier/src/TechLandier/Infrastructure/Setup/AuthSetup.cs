using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using TechLandier.Data;
using TechLandier.Features;

namespace TechLandier.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddStoreAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.LogoutPath = "/logout";
                o.AccessDeniedPath = "/email/verify";
                o.Cookie.Name = ".vis.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
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
            o.Cookie.Name = ".vis.af";
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
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            // Registration only (it is the endpoint with WithInertiaPrecognition, which answers "Precognition: true" without
            // running the handler). Those blur-time field checks get a roomier window of their own so they never spend the
            // real sign-up attempts; any other header value is a real submit and takes the strict window.
            o.AddPolicy("register", ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? RateLimitPartition.GetFixedWindowLimiter($"precognition:{ip}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) });
            });
            o.AddPolicy("uploads", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy("views", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
            // Mutations only: page loads, partial reloads and live-prop reloads (GET/HEAD) are never counted, so the account
            // and admin route groups can apply this policy wholesale.
            o.AddPolicy("writes", ctx => HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)
                ? RateLimitPartition.GetNoLimiter("reads")
                : RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
            // Public guest complaint form: unauthenticated and free text, so a tight window per client address.
            o.AddPolicy("guest-support", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
        });
        return builder;
    }
}
