using Inkwell.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Infrastructure.Setup;

public static class DataSetup
{
    public static WebApplicationBuilder AddBlogData(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var connectionString = config["DATABASE_URL"] ?? config.GetConnectionString("Default") ?? "Data Source=/data/app.db";
        EnsureDirectoryFor(connectionString);
        // The factory gives parallel work (Inertia .Parallel() props) a context of its own; request code gets a scoped one.
        builder.Services.AddDbContextFactory<AppDb>(o => o.UseSqlite(connectionString));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContext());
        builder.Services.AddSingleton(TimeProvider.System);

        // Keys must survive redeploys (Coolify volume at /data), or every deploy logs everyone out
        // and the encrypted Inertia state cookie becomes unreadable.
        builder.Services.AddDataProtection()
            .SetApplicationName("Inkwell")
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
