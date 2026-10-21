namespace Inkwell.Infrastructure;

public static class EnvironmentAliases
{
    public static void ApplyPlainNames(this IConfiguration config)
    {
        foreach (var (name, key) in new (string, string)[]
        {
            ("CRON_ENABLED", "Cron:Enabled"),
            ("CRON_RECOMMENDATION_DIGEST", "Cron:recommendation-digest"),
            ("CRON_SEARCH_RECONCILE", "Cron:search-reconcile"),
            ("CRON_INTEREST_PROFILES", "Cron:interest-profiles"),
            ("JOBS_CONCURRENCY", "Jobs:Concurrency"),
            ("DATA_PROTECTION_KEYS_PATH", "DataProtection:KeysPath"),
            ("APP_NAME", "App:Name"),
            ("APP_BASE_URL", "App:BaseUrl"),
            ("EMAIL_API_KEY", "Email:ApiKey"),
            ("EMAIL_FROM_ADDRESS", "Email:FromAddress"),
            ("EMAIL_FROM_NAME", "Email:FromName"),
            ("ANALYTICS_SALT", "Analytics:Salt"),
            ("SSR_EXECUTABLE", "Inertia:SsrExecutable"),
            ("CRON_BACKUP", "Cron:backup"),
            ("CRON_ORPHAN_UPLOADS", "Cron:orphan-uploads"),
            ("CRON_PRUNE", "Cron:prune"),
            ("CRON_UNVERIFIED_USERS", "Cron:unverified-users"),
            ("CRON_WEEKLY_DIGEST", "Cron:weekly-digest"),
        })
            if (config[name] is { } value) config[key] = value;
    }
}
