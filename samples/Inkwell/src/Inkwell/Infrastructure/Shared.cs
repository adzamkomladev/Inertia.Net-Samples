using System.Security.Claims;
using Inkwell.Data;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Infrastructure;

public sealed class AppOptions
{
    public string Name { get; set; } = "Inkwell";
    /// <summary>Public origin used in e-mail links, e.g. https://blog.example.com (no trailing slash).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5100";
}

public static class AuthPolicies
{
    /// <summary>Signed in with a confirmed e-mail. Required to post, comment, like and upload.</summary>
    public const string Verified = "verified";
    /// <summary>Claim added by the app's UserClaimsPrincipalFactory ("true" or "false").</summary>
    public const string EmailVerifiedClaim = "email_verified";
}

public static class RateLimits
{
    /// <summary>5/min per IP: login, register, resend verification.</summary>
    public const string Auth = "auth";
    /// <summary>30/min per user: comments, likes, uploads.</summary>
    public const string Writes = "writes";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? UserId(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public static bool IsVerified(this ClaimsPrincipal user) =>
        user.FindFirstValue(AuthPolicies.EmailVerifiedClaim) == "true";
}

public sealed record AuthUserDto(Guid Id, string Name, string Email, bool Verified, string? AvatarUrl, string Theme);

public sealed record SharedAuthDto(AuthUserDto? User);

/// <summary>The "auth" shared prop. One primary-key query per page so profile changes show up immediately.</summary>
public static class SharedAuth
{
    public static async Task<SharedAuthDto> LoadAsync(HttpContext ctx, CancellationToken ct)
    {
        var id = ctx.User.UserId();
        if (id is null) return new SharedAuthDto(null);

        var db = ctx.RequestServices.GetRequiredService<AppDb>();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new AuthUserDto(u.Id, u.DisplayName, u.Email!, u.EmailConfirmed, u.AvatarUrl, u.Preferences.Theme))
            .FirstOrDefaultAsync(ct);
        return new SharedAuthDto(user);
    }
}

public sealed record ErrorPageProps(int Status);

/// <summary>Renders the Error page for 403/404/500 so Inertia clients get a page instead of a blank response.</summary>
public static class ErrorPage
{
    public static IResult Render(int status) =>
        Inertia.Net.Inertia.Render("Error", new ErrorPageProps(status)).WithStatusCode(status);

    public static IResult NotFound() => Render(StatusCodes.Status404NotFound);
    public static IResult Forbidden() => Render(StatusCodes.Status403Forbidden);

    public static Task HandleStatusCodeAsync(HttpContext ctx)
    {
        var status = ctx.Response.StatusCode;
        var wantsPage = ctx.Request.Headers.ContainsKey("X-Inertia")
            || ctx.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        if (!wantsPage || status is not (403 or 404 or 500 or 503)) return Task.CompletedTask;
        return Render(status).ExecuteAsync(ctx);
    }
}

public static class Startup
{
    public static async Task InitializeAsync(IServiceProvider services, bool initializeStorage = true)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        var db = sp.GetRequiredService<AppDb>();
        await db.Database.MigrateAsync();
        // WAL is persistent per database file; readers no longer block the writer.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        if (!initializeStorage) return;
        try
        {
            await sp.GetRequiredService<Storage>().EnsureBucketsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Keep booting: uploads fail until storage is reachable, everything else works.
            logger.LogWarning(ex, "Object storage is not reachable; uploads and backups will fail until it is");
        }
    }
}
