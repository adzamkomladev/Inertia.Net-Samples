using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Inkwell.Data;
using Microsoft.AspNetCore.Identity;

namespace Inkwell.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddBlogAuth(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.LoginPath = "/login";
            o.ReturnUrlParameter = "returnUrl"; // what the Login page reads
            o.AccessDeniedPath = "/email/verify"; // the only policy beyond "signed in" is "verified"
            o.SlidingExpiration = true;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
        });
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDb>()
            .AddDefaultTokenProviders();
        services.AddAuthorizationBuilder()
            .AddPolicy("admin", p => p.RequireAuthenticatedUser().RequireAssertion(async context =>
            {
                if (context.Resource is not HttpContext http || http.User.UserId() is not { } id) return false;
                return await http.RequestServices.GetRequiredService<AppDb>().Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsAdmin, http.RequestAborted);
            }))
            .AddPolicy(AuthPolicies.Verified, p => p.RequireAuthenticatedUser().RequireClaim(AuthPolicies.EmailVerifiedClaim, "true"));

        services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(RateLimits.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
            // Registration only (it is the endpoint with WithInertiaPrecognition, which answers "Precognition: true" without
            // running the handler). Those blur-time field checks get a roomier window of their own so they never spend the
            // real sign-up attempts; any other header value is a real submit and takes the strict window.
            o.AddPolicy(RateLimits.Register, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? RateLimitPartition.GetFixedWindowLimiter($"precognition:{ip}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) });
            });
            o.AddPolicy(RateLimits.Writes, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserId()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
            o.OnRejected = async (ctx, ct) =>
            {
                // Inertia clients expect a redirect with something to show, not a bare 429.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                {
                    await Back().WithFlash("error", "Too many attempts. Please wait a minute and try again.").ExecuteAsync(ctx.HttpContext);
                    return;
                }
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            };
        });
        return builder;
    }
}
