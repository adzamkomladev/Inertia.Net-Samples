using KitchenSink.Data;
using KitchenSink.Features.Crm;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Infrastructure.Setup;

public static class DataSetup
{
    public static WebApplicationBuilder AddKitchenSinkData(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var contentRoot = builder.Environment.ContentRootPath;
        var sampleRoot = File.Exists(Path.Combine(contentRoot, "..", "..", "KitchenSink.slnx"))
            ? Path.GetFullPath(Path.Combine(contentRoot, "..", ".."))
            : contentRoot;
        var dataDir = Path.GetFullPath(config["App:DataDir"] is { Length: > 0 } d ? d : "App_Data", sampleRoot);
        Directory.CreateDirectory(dataDir);

        var databasePath = config["DATABASE_PATH"] ?? Path.Combine(dataDir, "kitchen-sink.db");
        databasePath = Path.GetFullPath(databasePath, sampleRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        builder.Services.AddPooledDbContextFactory<AppDb>(o => o.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContext());
        builder.Services.AddScoped<CrmService>();

        builder.Services.AddDataProtection()
            .SetApplicationName("KitchenSink")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

        return builder;
    }

    public static async Task InitializeKitchenSinkDataAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        await DemoSeeder.EnsureSeededAsync(db, cancellationToken);
    }
}
