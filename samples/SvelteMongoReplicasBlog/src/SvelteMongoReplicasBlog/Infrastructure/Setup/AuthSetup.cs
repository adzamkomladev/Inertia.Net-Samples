using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using StackExchange.Redis;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features;
using SvelteMongoReplicasBlog.Features.Discovery;

namespace SvelteMongoReplicasBlog.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddBlogAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var secureCookies = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.LogoutPath = "/logout";
                o.AccessDeniedPath = "/email/verify";
                o.Cookie.Name = ".smr.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = AuthHttp.ValidatePrincipal;
                // The cookie handler answers XHR (X-Requested-With) with a bare 401/403; Inertia visits should follow a redirect to the login/verify page instead.
                var redirectToLogin = o.Events.OnRedirectToLogin;
                var redirectToAccessDenied = o.Events.OnRedirectToAccessDenied;
                o.Events.OnRedirectToLogin = ctx => ctx.Request.Headers.ContainsKey("X-Inertia") ? InertiaRedirect(ctx) : redirectToLogin(ctx);
                o.Events.OnRedirectToAccessDenied = ctx => ctx.Request.Headers.ContainsKey("X-Inertia") ? InertiaRedirect(ctx) : redirectToAccessDenied(ctx);
                static Task InertiaRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx)
                {
                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                }
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("verified", p => p.RequireAuthenticatedUser().RequireClaim(AuthHelpers.VerifiedClaim, "true"));

        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = ".smr.af";
            o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Inertia clients expect a redirect with something to show, not a bare 429.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                    await Back().WithFlash("toast", new Toast("error", "Too many attempts. Please wait a minute and try again.")).ExecuteAsync(ctx.HttpContext);
            };
            o.AddPolicy("auth", ctx => Partition(ctx, "auth", 10, byUser: false));
            o.AddPolicy("uploads", ctx => Partition(ctx, "uploads", 30));
            o.AddPolicy("views", ctx => Partition(ctx, "views", 60));
            o.AddPolicy("writes", ctx => Partition(ctx, "writes", 120));
        });
        return builder;

        RateLimitPartition<string> Partition(HttpContext ctx, string policy, int limit, bool byUser = true)
        {
            var identity = (byUser ? ctx.User.UserIdOrNull()?.ToString() : null) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{ReplicaSetup.Namespace(builder)}:limits:{policy}:{identity}";
            return ReplicaSetup.Enabled(config)
                ? RateLimitPartition.Get(key, _ => new RedisFixedWindowRateLimiter(ctx.RequestServices.GetRequiredService<IConnectionMultiplexer>(), key, limit))
                : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
        }
    }
}
