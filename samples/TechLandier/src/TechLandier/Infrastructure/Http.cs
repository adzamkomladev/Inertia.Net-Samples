using System.Security.Claims;

namespace TechLandier.Infrastructure;

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
            ("CRON_SEARCH_RECONCILE", "Cron:search-reconcile"),
            ("CRON_SALES_ROLLUP", "Cron:sales-rollup"),
            ("CRON_BNPL_REMINDERS", "Cron:bnpl-reminders"),
            ("CRON_BNPL_OVERDUE", "Cron:bnpl-overdue"),
            ("CRON_BNPL_STATEMENT", "Cron:bnpl-statement"),
            ("CRON_TICKET_ESCALATION", "Cron:ticket-escalation"),
            ("CRON_ORDER_TIMEOUT", "Cron:order-timeout"),
            ("DATABASE_URL", "ConnectionStrings:Default"), ("DATA_DIR", "App:DataDir"), ("APP_NAME", "App:Name"),
            ("APP_BASE_URL", "App:BaseUrl"), ("ANALYTICS_SALT", "App:AnalyticsSalt"), ("TRUSTED_PROXY_NETWORKS", "App:TrustedProxyNetworks"),
            ("S3_ENDPOINT", "Storage:ServiceUrl"), ("S3_REGION", "Storage:Region"), ("S3_BUCKET", "Storage:Bucket"),
            ("S3_BACKUP_BUCKET", "Storage:BackupBucket"), ("S3_ACCESS_KEY", "Storage:AccessKey"), ("S3_SECRET_KEY", "Storage:SecretKey"),
            ("S3_PUBLIC_URL", "Storage:PublicBaseUrl"), ("S3_PUBLIC_ENDPOINT", "Storage:PublicServiceUrl"), ("STORAGE_FORCED_PATH_STYLE", "Storage:ForcePathStyle"),
            ("EMAIL_API_KEY", "Maileroo:ApiKey"), ("EMAIL_FROM_ADDRESS", "Maileroo:From"), ("EMAIL_FROM_NAME", "Maileroo:FromName"),
            ("JOBS_CONCURRENCY", "Jobs:Concurrency"), ("SSR_ENABLED", "Inertia:Ssr"), ("SSR_EXECUTABLE", "Inertia:SsrExecutable"),
            ("SSR_TIMEOUT_SECONDS", "Inertia:SsrTimeoutSeconds"),
            ("CRON_PRUNE_VIEWS", "Cron:prune-product-views"), ("CRON_ORPHAN_ASSETS", "Cron:orphan-assets"),
            ("CRON_PRUNE_UNVERIFIED", "Cron:prune-unverified"), ("CRON_PRUNE_JOBS", "Cron:prune-jobs"),
        })
            if (config[name] is { } value && !Explicit(key)) config[key] = value;
    }
}
