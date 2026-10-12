using ReactSqliteBlog.Data;
using ReactSqliteBlog.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ReactSqliteBlog.Tests;

/// <summary>A migrated temp-file SQLite database plus a minimal container (AppDb, JobQueue, JobRegistry, fake clock).</summary>
public sealed class TestServices : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"inertiablog-test-{Guid.NewGuid():N}.db");

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    public ServiceProvider Provider { get; }

    private TestServices(Action<IServiceCollection>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={_path};Foreign Keys=True"));
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<JobQueue>();
        services.AddSingleton(new JobRegistry());
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider();
    }

    public static async Task<TestServices> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var s = new TestServices(configure);
        await using var scope = s.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
        return s;
    }

    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    public AppDb NewDb() => Provider.CreateScope().ServiceProvider.GetRequiredService<AppDb>();

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
