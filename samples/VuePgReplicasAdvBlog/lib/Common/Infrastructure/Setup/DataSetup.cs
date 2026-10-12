using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Infrastructure.Setup;

public static class DataSetup
{
    public static TBuilder AddCommonData<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var config = builder.Configuration;
        // Only local mail capture needs a directory; shared keys live in PostgreSQL.
        var contentRoot = builder.Environment.ContentRootPath;
        var sampleRoot = File.Exists(Path.Combine(contentRoot, "..", "..", "VuePgReplicasAdvBlog.slnx"))
            ? Path.GetFullPath(Path.Combine(contentRoot, "..", "..")) : contentRoot;
        var dataDir = Path.GetFullPath(config["App:DataDir"] is { Length: > 0 } d ? d : "storage", sampleRoot);
        Directory.CreateDirectory(dataDir);
        var connectionString = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL is required.");

        builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(DatabaseConnection.Parse(connectionString)));

        builder.Services.AddDataProtection()
            .SetApplicationName(ReplicaSetup.Namespace(builder))
            .PersistKeysToDbContext<AppDb>();
        return builder;
    }
}
