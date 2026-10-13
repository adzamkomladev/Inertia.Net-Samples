using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Tests;

public sealed class TickerRedisTests
{
    [Fact]
    public async Task Two_workers_share_persisted_jobs_and_only_one_can_claim_each_job()
    {
        ServiceProvider Node(string id)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_URL"] = Environment.GetEnvironmentVariable("INERTIA_TEST_REDIS") ?? "redis://127.0.0.1:6387",
                ["INSTANCE_ID"] = id,
                ["Replicas:Namespace"] = "ticker-tests"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(config);
            services.AddCron(config);
            return services.BuildServiceProvider();
        }

        using var first = Node("ticker-test-a");
        using var second = Node("ticker-test-b");
        var a = first.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var b = second.GetRequiredService<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
        var job = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "analytics-rollup", ExecutionTime = DateTime.UtcNow };
        await a.AddTimeTickers([job]);
        try
        {
            var readA = await a.GetTimeTickerById(job.Id);
            var readB = await b.GetTimeTickerById(job.Id);
            Assert.NotNull(readB);
            Assert.Equal(job.Id, readB.Id);
            Assert.Equal(readA.CreatedAt, readB.CreatedAt);
            Assert.Equal(readA.UpdatedAt, readB.UpdatedAt);
            var redis = first.GetRequiredKeyedService<StackExchange.Redis.IConnectionMultiplexer>("tickerq").GetDatabase();
            using var storedJson = System.Text.Json.JsonDocument.Parse((string)(await redis.StringGetAsync("tq:tt:" + job.Id))!);
            Assert.Equal(readA.UpdatedAt.ToString("O"), storedJson.RootElement.GetProperty("UpdatedAt").GetString());

            static async Task<TimeTickerEntity[]> Collect(IAsyncEnumerable<TimeTickerEntity> queue)
            {
                var jobs = new List<TimeTickerEntity>();
                await foreach (var item in queue) jobs.Add(item);
                return jobs.ToArray();
            }

            var claimed = await Task.WhenAll(Collect(a.QueueTimeTickers([readA])), Collect(b.QueueTimeTickers([readB])));
            var acquiredJson = (string)(await redis.StringGetAsync("tq:tt:" + job.Id))!;
            System.Text.Json.JsonSerializer.Deserialize<TimeTickerEntity>(acquiredJson, new System.Text.Json.JsonSerializerOptions { TypeInfoResolver = CronJsonContext.Default });
            Assert.Single(claimed.SelectMany(items => items));
            var stored = await b.GetTimeTickerById(job.Id);
            Assert.Contains(stored.LockHolder, new[] { "ticker-test-a", "ticker-test-b" });
        }
        finally { await a.RemoveTimeTickers([job.Id]); }
    }
}
