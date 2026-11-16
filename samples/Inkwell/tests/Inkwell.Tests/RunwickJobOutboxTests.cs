using Inkwell.Data;
using Inkwell.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Runwick;

namespace Inkwell.Tests;

/// <summary>The real Runwick SQLite store on the app's database file: staged jobs share the business transaction.</summary>
public class RunwickJobOutboxTests
{
    static async Task<(ServiceProvider Provider, string Path)> CreateAsync()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"inkwell-runwick-{Guid.NewGuid():N}.db");
        var cs = $"Data Source={path};Foreign Keys=True";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDb>(o => o.UseSqlite(cs));
        services.AddRunwick(r =>
        {
            r.Application("inkwell").UseSqlite(cs).Queue("mail");
            r.AddJob<SendVerificationEmail>().Named("mail.verification").ToQueue("mail");
        });
        services.AddSingleton<IJobOutbox, RunwickJobOutbox>();
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
        await RunwickSqliteMigrations.MigrateAsync(cs);
        // As in the app: Runwick registers its application while the host starts, before any request stages work.
        foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(CancellationToken.None);
        return (provider, path);
    }

    static async Task<long> JobCountAsync(string path)
    {
        await using var c = new SqliteConnection($"Data Source={path}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM runwick_jobs WHERE job_name = 'mail.verification'";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Staged_job_commits_with_the_transaction_and_disappears_with_a_rollback()
    {
        var (provider, path) = await CreateAsync();
        try
        {
            var outbox = provider.GetRequiredService<IJobOutbox>();
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                await using var tx = await db.Database.BeginTransactionAsync();
                await outbox.StageAsync(db, new SendVerificationEmail(Guid.NewGuid()));
                await tx.RollbackAsync();
            }
            Assert.Equal(0, await JobCountAsync(path));

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                await using var tx = await db.Database.BeginTransactionAsync();
                await outbox.StageAsync(db, new SendVerificationEmail(Guid.NewGuid()));
                await tx.CommitAsync();
            }
            Assert.Equal(1, await JobCountAsync(path));
        }
        finally
        {
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Staging_outside_a_transaction_is_refused()
    {
        var (provider, path) = await CreateAsync();
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.GetRequiredService<IJobOutbox>().StageAsync(db, new SendVerificationEmail(Guid.NewGuid())));
        }
        finally
        {
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
