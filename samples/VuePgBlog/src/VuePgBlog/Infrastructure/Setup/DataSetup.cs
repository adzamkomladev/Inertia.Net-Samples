using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Infrastructure.Setup;

public static class DataSetup
{
    public static WebApplicationBuilder AddStoreData(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        // Everything that must survive a redeploy lives here (Coolify persistent storage mounts /data).
        var contentRoot = builder.Environment.ContentRootPath;
        var sampleRoot = File.Exists(Path.Combine(contentRoot, "..", "..", "VuePgBlog.slnx"))
            ? Path.GetFullPath(Path.Combine(contentRoot, "..", "..")) : contentRoot;
        var dataDir = Path.GetFullPath(config["App:DataDir"] is { Length: > 0 } d ? d : "storage", sampleRoot);
        Directory.CreateDirectory(dataDir);
        var connectionString = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required.");

        builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(DatabaseConnection.Parse(connectionString)));

        builder.Services.AddDataProtection()
            .SetApplicationName("VuePgBlog")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
        return builder;
    }
}
