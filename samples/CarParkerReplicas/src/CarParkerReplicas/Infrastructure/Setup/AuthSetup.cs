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
                // Inertia visits follow the login/verify redirect like browser navigations (303 after POST/PUT/PATCH/DELETE);
                // fetch calls and Precognition requests keep the handler events configured above.
                o.UseInertiaRedirects();
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
                // fetch callers (uploads) read title/errors from a problem document.
                else if (ctx.HttpContext.Request.Headers.Accept.ToString().Contains("json", StringComparison.OrdinalIgnoreCase))
                    await Results.Problem(title: "Too many requests. Please wait a minute and try again.", statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(ctx.HttpContext);
            };
            // Per-address windows are sized for a whole room behind one NAT (the playbook's ×100 of a single user's
            // budget): a campus or office shares one public IP, so a per-user-sized window would lock them all out
            // together. The per-identity limits below remain the abuse control; sign-in attempts are also guarded per
            // account in AuthService, because anonymous callers have no identity to key on.
            o.AddPolicy("auth", ctx => Partition(ctx, "auth", 1_000, byUser: false));
            // Registration only (it is the endpoint with WithInertiaPrecognition, which answers "Precognition: true" without
            // running the handler). Those blur-time field checks get a roomier window of their own so they never spend the
            // real sign-up attempts; any other header value is a real submit and takes the strict window.
            o.AddPolicy("register", ctx =>
            {
                var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? Partition(ctx, "register:precognition", 6_000, partitionIdentity: $"precognition:{ip}")
                    : Partition(ctx, "register", 1_000, partitionIdentity: ip);
            });
            // Verification mail resends: per signed-in user (and per IP), a handful a minute — the identity is the abuse control.
            o.AddPolicy("verification", ctx => Partition(ctx, "verification", 3,
                partitionIdentity: $"{ctx.User.UserIdOrNull()}:{ctx.Connection.RemoteIpAddress}"));
            // Password reset requests and submits: the per-address window is the room budget; the reset token is the
            // single-use identity, so spraying mail stays bounded by token generation costs.
            o.AddPolicy("password-reset", ctx => Partition(ctx, "password-reset", 500,
                partitionIdentity: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
            // Booking creation: Precognition field checks get their own roomier window so they never spend real bookings.
            o.AddPolicy("bookings", ctx =>
            {
                var who = ctx.User.UserIdOrNull()?.ToString() ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return string.Equals(ctx.Request.Headers["Precognition"], "true", StringComparison.Ordinal)
                    ? Partition(ctx, "bookings:precognition", 120, partitionIdentity: $"precognition:{who}")
                    : Partition(ctx, "bookings", 20, partitionIdentity: who);
            });
            // Signed-in callers keep the tight per-user budget (abuse control); a guest's fallback is the room-sized
            // address window, so a hall of anonymous visitors does not drain one shared 30/min bucket.
            o.AddPolicy("uploads", ctx => ctx.User.UserIdOrNull() is { } id
                ? Partition(ctx, "uploads", 30, partitionIdentity: $"user:{id}")
                : Partition(ctx, "uploads", 3_000, partitionIdentity: $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"));
            o.AddPolicy("views", ctx => ctx.User.UserIdOrNull() is { } id
                ? Partition(ctx, "views", 60, partitionIdentity: $"user:{id}")
                : Partition(ctx, "views", 6_000, partitionIdentity: $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"));
            o.AddPolicy("writes", ctx => ctx.User.UserIdOrNull() is { } id
                ? Partition(ctx, "writes", 120, partitionIdentity: $"user:{id}")
                : Partition(ctx, "writes", 12_000, partitionIdentity: $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"));
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
