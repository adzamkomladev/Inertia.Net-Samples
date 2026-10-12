using System.Threading.RateLimiting;
using StackExchange.Redis;
using ReactSqliteReplicasBlog.Data;
using Microsoft.AspNetCore.Identity;

namespace ReactSqliteReplicasBlog.Infrastructure.Setup;

/// <summary>Identity, authorization, antiforgery and rate-limiting wiring.</summary>
public static class AuthSetup
{
    public static WebApplicationBuilder AddBlogAuth(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;
        var secure = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = ".rsr.auth";
            o.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
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
            .AddPolicy(AuthPolicies.Verified, p => p.RequireAuthenticatedUser().RequireClaim(AuthPolicies.EmailVerifiedClaim, "true"));

        services.AddAntiforgery(o => { o.HeaderName = "X-XSRF-TOKEN"; o.Cookie.Name = ".rsr.af";
            o.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest; });
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(RateLimits.Auth, ctx => Partition(ctx, "auth", 5, false));
            o.AddPolicy(RateLimits.Writes, ctx => Partition(ctx, "writes", 120));
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        RateLimitPartition<string> Partition(HttpContext ctx, string policy, int limit, bool byUser = true)
        {
            var identity = (byUser ? ctx.User.UserId()?.ToString() : null) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var key = $"{ReplicaSetup.Namespace(builder)}:limits:{policy}:{identity}";
            return ReplicaSetup.Enabled(config)
                ? RateLimitPartition.Get(key, _ => new RedisFixedWindowRateLimiter(ctx.RequestServices.GetRequiredService<IConnectionMultiplexer>(), key, limit))
                : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
        }
    }
}
