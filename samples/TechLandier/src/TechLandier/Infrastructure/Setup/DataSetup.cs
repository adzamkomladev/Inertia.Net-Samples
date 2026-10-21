using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Infrastructure.Setup;

public static class DataSetup
{
    public static WebApplicationBuilder AddStoreData(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        // Persistent data (data-protection keys, captured mail) lives here.
        var contentRoot = builder.Environment.ContentRootPath;
        var sampleRoot = File.Exists(Path.Combine(contentRoot, "..", "..", "TechLandier.slnx"))
            ? Path.GetFullPath(Path.Combine(contentRoot, "..", "..")) : contentRoot;
        var dataDir = Path.GetFullPath(config["App:DataDir"] is { Length: > 0 } d ? d : "storage", sampleRoot);
        Directory.CreateDirectory(dataDir);
        var connectionString = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required.");

        // The factory gives parallel Inertia loaders their own context; handlers and services share one scoped AppDb per request.
        builder.Services.AddDbContextFactory<AppDb>(o => o.UseNpgsql(DatabaseConnection.Parse(connectionString)));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContext());

        builder.Services.AddDataProtection()
            .SetApplicationName("TechLandier")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
        return builder;
    }
}
