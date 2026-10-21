using System.Security.Claims;

namespace SvelteMongoBlog.Infrastructure;

public static class HttpExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in."));

    public static Guid? UserIdOrNull(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// 403 for "you don't own this". Not Results.Forbid(): that goes through the cookie handler's AccessDeniedPath.
    public static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);

    public static string? AppBaseUrl(this IConfiguration config) => config["App:BaseUrl"] is { } url && !string.IsNullOrWhiteSpace(url) ? url.Trim().TrimEnd('/') : null;
}

/// Orders Guids the way MongoDB orders Standard UUIDs (bytewise, which is chronological for UUIDv7), so in-memory merges agree with keyset filters.
public sealed class GuidByteOrder : IComparer<Guid>
{
    public static readonly GuidByteOrder Instance = new();
    public int Compare(Guid a, Guid b) => a.ToByteArray(true).AsSpan().SequenceCompareTo(b.ToByteArray(true));
}

/// Flash payload read by the client's toaster (`page.flash.toast`).
public sealed record Toast(string Type, string Message);

public static class EnvironmentAliases
{
    /// Plain env names (DATABASE_URL, S3_*, ...) override appsettings*.json, but never a canonical `Section:Key`/`Section__Key`
    /// set by a real env var, command line or UseSetting. Call right after CreateBuilder.
    public static void ApplyPlainNames(this IConfigurationManager config)
    {
        bool Explicit(string key) => ((IConfigurationRoot)config).Providers
            .Any(p => p is not Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider && p.TryGet(key, out var v) && !string.IsNullOrEmpty(v));

        foreach (var (name, key) in new (string, string)[]
        {
            ("CRON_ENABLED", "Cron:Enabled"),
            ("CRON_BACKUP", "Cron:backup"),
            ("CRON_PRUNE_JOBS", "Cron:prune-jobs"),
            ("CRON_ORPHAN_ASSETS", "Cron:orphan-assets"),
            ("CRON_PRUNE_UNVERIFIED", "Cron:prune-unverified"),
            ("CRON_SEARCH_LOT_RECONCILE", "Cron:search-lot-reconcile"),
            ("CRON_LOTS_STAT_ROLLUP", "Cron:lots-stat-rollup"),
            ("CRON_RESERVATION_SWEEPER", "Cron:reservation-sweeper"),
            ("CRON_NO_SHOW_CHECK", "Cron:no-show-check"),
            ("CRON_OVERSTAY_CHECK", "Cron:overstay-check"),
            ("CRON_POLICY_EXPIRY", "Cron:policy-expiry"),
            ("DATABASE_URL", "ConnectionStrings:Default"), ("DATA_DIR", "App:DataDir"), ("APP_NAME", "App:Name"),
            ("APP_BASE_URL", "App:BaseUrl"), ("ANALYTICS_SALT", "App:AnalyticsSalt"), ("TRUSTED_PROXY_NETWORKS", "App:TrustedProxyNetworks"),
            ("S3_ENDPOINT", "Storage:ServiceUrl"), ("S3_REGION", "Storage:Region"), ("S3_BUCKET", "Storage:Bucket"),
            ("S3_BACKUP_BUCKET", "Storage:BackupBucket"), ("S3_ACCESS_KEY", "Storage:AccessKey"), ("S3_SECRET_KEY", "Storage:SecretKey"),
            ("S3_PUBLIC_URL", "Storage:PublicBaseUrl"), ("S3_PUBLIC_ENDPOINT", "Storage:PublicServiceUrl"), ("STORAGE_FORCED_PATH_STYLE", "Storage:ForcePathStyle"),
            ("EMAIL_API_KEY", "Maileroo:ApiKey"), ("EMAIL_FROM_ADDRESS", "Maileroo:From"), ("EMAIL_FROM_NAME", "Maileroo:FromName"),
            ("JOBS_CONCURRENCY", "Jobs:Concurrency"), ("SSR_ENABLED", "Inertia:Ssr"), ("SSR_EXECUTABLE", "Inertia:SsrExecutable"),
            ("SSR_TIMEOUT_SECONDS", "Inertia:SsrTimeoutSeconds"),
        })
            if (config[name] is { } value && !Explicit(key)) config[key] = value;
    }
}
