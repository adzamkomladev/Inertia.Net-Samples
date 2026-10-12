using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features;
using SvelteMongoBlog.Features.Discovery;

namespace SvelteMongoBlog.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddBlogAuth(this WebApplicationBuilder builder)
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
                o.Events.OnValidatePrincipal = AuthService.ValidatePrincipal;
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
            o.Cookie.Name = ".vis.af";
        });

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                // Inertia clients expect a redirect with something to show, not a bare 429.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                    await Back().WithFlash("toast", new Toast("error", "Too many attempts. Please wait a minute and try again.")).ExecuteAsync(ctx.HttpContext);
            };
            o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy("uploads", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy("views", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy("writes", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
        });
        return builder;
    }
}
