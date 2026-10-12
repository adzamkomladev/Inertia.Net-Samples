using ReactSqliteBlog.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteBlog.Infrastructure.Setup;

/// <summary>Database, time and data-protection wiring.</summary>
public static class DataSetup
{
    public static WebApplicationBuilder AddBlogData(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var connectionString = config["DATABASE_URL"] ?? config.GetConnectionString("Default") ?? "Data Source=/data/app.db";
        EnsureDirectoryFor(connectionString);
        builder.Services.AddDbContext<AppDb>(o => o.UseSqlite(connectionString));
        builder.Services.AddSingleton(TimeProvider.System);

        // Keys must survive redeploys (Coolify volume at /data), or every deploy logs everyone out
        // and the encrypted Inertia state cookie becomes unreadable.
        builder.Services.AddDataProtection()
            .SetApplicationName("ReactSqliteBlog")
            .PersistKeysToFileSystem(Directory.CreateDirectory(config["DataProtection:KeysPath"] ?? "/data/keys"));

        builder.Services.Configure<AppOptions>(config.GetSection("App"));
        return builder;
    }

    private static void EnsureDirectoryFor(string connectionString)
    {
        var path = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
