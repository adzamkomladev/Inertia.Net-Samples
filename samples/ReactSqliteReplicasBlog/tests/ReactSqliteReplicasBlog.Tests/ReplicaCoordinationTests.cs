using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReactSqliteReplicasBlog.Cron;
using ReactSqliteReplicasBlog.Data;
using TickerQ.Utilities;

namespace ReactSqliteReplicasBlog.Tests;

public class ReplicaCoordinationTests
{
    [Theory]
    [InlineData("0 0 0 1 1 *", "2026-01-01T00:00:00Z")]
    [InlineData("0 0 0 29 2 *", "2024-02-29T00:00:00Z")]
    [InlineData("* * * * * *", "2026-10-12T12:00:00Z")]
    public void Cron_claims_use_the_latest_due_slot_including_long_schedules(string expression, string expected)
    {
        var schedule = NCrontab.CrontabSchedule.Parse(expression, new NCrontab.CrontabSchedule.ParseOptions { IncludingSeconds = true });
        var now = DateTime.Parse("2026-10-12T12:00:00Z").ToUniversalTime();
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), CronLeaseRunner.LatestDue(schedule, now));
    }

    [Fact]
    public async Task Independent_hosts_claim_one_cron_occurrence_and_keep_it_completed()
    {
        using var first = new BlogFactory();
        var connection = first.WithDb(db => db.Database.GetConnectionString()!);
        using var second = new BlogFactory(b => b.UseSetting("DATABASE_URL", connection));
        _ = second.Services;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var task = new CronTask("replica-test", CronExpression.Parse("* * * * *"), async (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        });
        // One scheduled slot, stable while both hosts race and after completion.
        const string expression = "0 0 0 * * *";
        var running = CronLeaseRunner.RunAsync(task, expression, first.Services, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await CronLeaseRunner.RunAsync(task, expression, second.Services, default);
            Assert.Equal(1, calls);
        }
        finally { release.TrySetResult(); await running; }
        await CronLeaseRunner.RunAsync(task, expression, second.Services, default);
        Assert.Equal(1, calls);
        first.WithDb(db => { Assert.NotNull(db.CronLeases.Single().LastRunAt); Assert.Null(db.CronLeases.Single().Token); return true; });
    }

    [Fact]
    public void Native_sqlite_has_the_multi_connection_WAL_fix()
    {
        using var factory = new BlogFactory();
        factory.WithDb(db =>
        {
            var version = db.Database.SqlQueryRaw<string>("SELECT sqlite_version() AS Value").Single();
            Assert.True(Version.Parse(version) >= new Version(3, 51, 3), version);
            Assert.Equal("wal", db.Database.SqlQueryRaw<string>("PRAGMA journal_mode").AsEnumerable().Single());
            return true;
        });
    }
}
