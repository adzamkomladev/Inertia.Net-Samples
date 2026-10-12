namespace VuePgReplicasAdvBlog.Infrastructure;

public static class EnvironmentAliases
{
    /// Plain env names (DATABASE_URL, S3_*, ...) override appsettings*.json, but never a canonical `Section:Key`/`Section__Key`
    /// set by a real env var, command line or UseSetting. Call right after CreateBuilder.
    public static void ApplyPlainNames(this IConfigurationManager config, params (string Name, string Key)[] applicationAliases)
    {
        bool Explicit(string key) => ((IConfigurationRoot)config).Providers
            .Any(p => p is not Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider && p.TryGet(key, out var v) && !string.IsNullOrEmpty(v));

        (string, string)[] sharedAliases =
        [
            ("CRON_ENABLED", "Cron:Enabled"),
            ("CRON_BACKUP", "Cron:backup"),
            ("CRON_RECOMMENDATION_DIGEST", "Cron:recommendation-digest"),
            ("CRON_SEARCH_RECONCILE", "Cron:search-reconcile"),
            ("CRON_INTEREST_PROFILES", "Cron:interest-profiles"),
            ("DATABASE_URL", "ConnectionStrings:Default"), ("DATA_DIR", "App:DataDir"), ("APP_NAME", "App:Name"),
            ("APP_BASE_URL", "App:BaseUrl"), ("ANALYTICS_SALT", "App:AnalyticsSalt"), ("TRUSTED_PROXY_NETWORKS", "App:TrustedProxyNetworks"),
            ("S3_ENDPOINT", "Storage:ServiceUrl"), ("S3_REGION", "Storage:Region"), ("S3_BUCKET", "Storage:Bucket"),
            ("S3_BACKUP_BUCKET", "Storage:BackupBucket"), ("S3_ACCESS_KEY", "Storage:AccessKey"), ("S3_SECRET_KEY", "Storage:SecretKey"),
            ("S3_PUBLIC_URL", "Storage:PublicBaseUrl"), ("S3_PUBLIC_ENDPOINT", "Storage:PublicServiceUrl"), ("STORAGE_FORCED_PATH_STYLE", "Storage:ForcePathStyle"),
            ("EMAIL_API_KEY", "Maileroo:ApiKey"), ("EMAIL_FROM_ADDRESS", "Maileroo:From"), ("EMAIL_FROM_NAME", "Maileroo:FromName"),
            ("JOBS_CONCURRENCY", "Jobs:Concurrency"),
            ("CRON_ANALYTICS_ROLLUP", "Cron:analytics-rollup"), ("CRON_PRUNE_VIEWS", "Cron:prune-views"), ("CRON_ORPHAN_ASSETS", "Cron:orphan-assets"),
            ("CRON_PRUNE_UNVERIFIED", "Cron:prune-unverified"), ("CRON_PRUNE_JOBS", "Cron:prune-jobs"), ("CRON_WEEKLY_DIGEST", "Cron:weekly-digest"),
        ];
        foreach (var (name, key) in sharedAliases.Concat(applicationAliases))
            if (config[name] is { } value && !Explicit(key)) config[key] = value;
    }
}
