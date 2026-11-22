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
            // Inertia visits follow the login/verify redirect like browser navigations (303 after POST/PUT/PATCH/DELETE).
            o.UseInertiaRedirects();
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
            // Per-address limits are sized for a whole room behind one NAT (the playbook's ×100 of a single user's
            // budget): a campus or office shares one public IP, so a per-user-sized window would lock them all out
            // together. The per-identity abuse control stays: five failed sign-ins lock the account for 15 minutes.
            o.AddPolicy(RateLimits.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 500, Window = TimeSpan.FromMinutes(1) }));
            // Registration only (it is the endpoint with WithInertiaPrecognition, which answers "Precognition: true" without
            // running the handler). Those blur-time field checks get a roomier window of their own so they never spend the
            // real sign-up attempts; any other header value is a real submit and takes the strict window.
            o.AddPolicy(RateLimits.Register, ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? RateLimitPartition.GetFixedWindowLimiter($"precognition:{ip}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 6_000, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = 500, Window = TimeSpan.FromMinutes(1) });
            });
            // Writes stay per identity (the abuse limit); a signed-out write falls back to the room-sized address window.
            o.AddPolicy(RateLimits.Writes, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.UserId()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
            o.OnRejected = async (ctx, ct) =>
            {
                // Inertia clients expect a redirect with something to show, not a bare 429. A rejected write comes back as
                // an error (not a success) so forms keep their unsaved state and the leave-page guard still protects it.
                if (ctx.HttpContext.Request.Headers.ContainsKey("X-Inertia"))
                {
                    const string message = "Too many attempts. Please wait a minute and try again.";
                    await Back().WithErrors(new Dictionary<string, string> { ["rateLimit"] = message }).WithFlash("error", message)
                        .ExecuteAsync(ctx.HttpContext);
                    return;
                }
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            };
        });
        return builder;
    }
}
