using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features;

namespace CarParker.Infrastructure.Setup;

public static class DataSetup
{
    public static WebApplicationBuilder AddBlogData(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        // Everything that must survive a redeploy lives here (Coolify persistent storage mounts /data).
        var contentRoot = builder.Environment.ContentRootPath;
        var sampleRoot = File.Exists(Path.Combine(contentRoot, "..", "..", "CarParkerReplicas.slnx"))
            ? Path.GetFullPath(Path.Combine(contentRoot, "..", "..")) : contentRoot;
        var dataDir = Path.GetFullPath(config["App:DataDir"] is { Length: > 0 } d ? d : "storage", sampleRoot);
        Directory.CreateDirectory(dataDir);
        var mongoUrl = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("DATABASE_URL must be configured.");
        builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoUrl));
        builder.Services.AddSingleton<AppDb>();

        // The key ring follows Redis whenever Redis is configured, even in the one-shot --initialize
        // process where Replicas:Enabled=false. Otherwise auth/antiforgery/state cookies would be
        // encrypted with different keys by the initializer and the app replicas.
        if (ReplicaSetup.HasRedis(config))
            ReplicaSetup.AddSharedRedis(builder);
        else
            builder.Services.AddDataProtection()
                .SetApplicationName(ReplicaSetup.Namespace(builder))
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));
        return builder;
    }
}
