namespace Inkwell.Infrastructure;

public static class EnvironmentAliases
{
    /// <summary>Lets deployments use plain variable names (APP_BASE_URL) for nested configuration keys (App:BaseUrl).</summary>
    public static void ApplyPlainNames(this IConfiguration config)
    {
        foreach (var (name, key) in new (string, string)[]
        {
            ("APP_NAME", "App:Name"),
            ("APP_BASE_URL", "App:BaseUrl"),
            ("ANALYTICS_SALT", "Analytics:Salt"),
            ("EMAIL_API_KEY", "Email:ApiKey"),
            ("EMAIL_FROM_ADDRESS", "Email:FromAddress"),
            ("EMAIL_FROM_NAME", "Email:FromName"),
            ("JOBS_CONCURRENCY", "Jobs:Concurrency"), ("JOBS_ENABLED", "Jobs:Enabled"),
            ("DATA_PROTECTION_KEYS_PATH", "DataProtection:KeysPath"),
            ("SSR_EXECUTABLE", "Inertia:SsrExecutable"),
            ("CRON_ENABLED", "Cron:Enabled"),
            ("CRON_ORPHAN_UPLOADS", "Cron:orphan-uploads"),
            ("CRON_PRUNE", "Cron:prune"),
            ("CRON_UNVERIFIED_USERS", "Cron:unverified-users"),
            ("CRON_WEEKLY_DIGEST", "Cron:weekly-digest"),
            ("CRON_RECOMMENDATION_DIGEST", "Cron:recommendation-digest"),
            ("CRON_INTEREST_PROFILES", "Cron:interest-profiles"),
        })
            if (config[name] is { } value) config[key] = value;
    }
}
