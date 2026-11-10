using Npgsql;
using TechLandier.Data;

namespace TechLandier.Infrastructure.Setup;

/// <summary>
/// Resolves the PostgreSQL connection Runwick uses. Runwick appends
/// <c>Options=-c plan_cache_mode=force_custom_plan</c>, which Neon's <c>-pooler</c> endpoint rejects.
/// Any Neon pooled host is therefore rewritten to the direct (unpooled) host.
/// `RUNWICK_DATABASE_URL` can override the base connection explicitly.
/// </summary>
public static class RunwickConnection
{
    public static string Resolve(IConfiguration config)
    {
        var explicitUrl = config["Runwick:DatabaseUrl"];
        var value = !string.IsNullOrWhiteSpace(explicitUrl)
            ? explicitUrl
            : config["DATABASE_URL"] ?? config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("DATABASE_URL is required.");

        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.Parse(value));
        var host = connection.Host;
        if (host is not null
            && host.EndsWith(".neon.tech", StringComparison.OrdinalIgnoreCase)
            && host.Contains("-pooler.", StringComparison.OrdinalIgnoreCase))
        {
            connection.Host = host.Replace("-pooler.", ".", StringComparison.OrdinalIgnoreCase);
        }

        return connection.ConnectionString;
    }
}
