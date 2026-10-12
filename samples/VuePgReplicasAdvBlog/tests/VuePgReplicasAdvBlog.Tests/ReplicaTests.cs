using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCrontab;
using StackExchange.Redis;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Tests;

// Independent service containers/key caches share only PostgreSQL and Redis.
public sealed class ReplicaFixture : IAsyncLifetime
{
    public AppFactory First { get; private set; } = null!;
    public AppFactory Second { get; private set; } = null!;
    public string Namespace { get; } = "replica-tests:" + Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        var redis = Environment.GetEnvironmentVariable("INERTIA_TEST_REDIS") ?? "redis://127.0.0.1:6387";
        First = new AppFactory(null, redis, Namespace);
        // Generate the shared key before the other host loads its independent key cache.
        First.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("warmup");
        Second = new AppFactory(First.Connection, redis, Namespace);
        using var response = await Second.Client().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        if (Second is not null) await Second.DisposeAsync();
        if (First is not null) await First.DisposeAsync();
    }
}

public sealed class ReplicaTests(ReplicaFixture replicas) : IClassFixture<ReplicaFixture>
{
    [Fact]
    public async Task Login_and_antiforgery_tokens_from_one_host_work_on_another()
    {
        var first = replicas.First;
        var second = replicas.Second;
        var email = $"replica-cookie-{Guid.NewGuid():N}@yopmail.com";
        var (client, _) = await first.SignUpAsync(email);
        using var login = await first.SendAsync(client, "/login", new { email, password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var authCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".vpgmra.auth=")).Split(';')[0];
        using var fresh = first.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        fresh.DefaultRequestHeaders.Add("Cookie", authCookie);
        using var page = await first.InertiaGetAsync(fresh, "/posts");
        var cookies = page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToArray();
        var token = Uri.UnescapeDataString(cookies.Single(c => c.StartsWith("XSRF-TOKEN="))["XSRF-TOKEN=".Length..]);
        using var moved = second.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        moved.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Append(authCookie)));
        using var editor = await moved.GetAsync("/posts/create");
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
        var title = "Cross-host form " + Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/posts") { Content = JsonContent.Create(new { title, body = "Persisted by the second host" }) };
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var saved = await moved.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.NotEqual(Guid.Empty, await first.PostIdAsync(title));
    }

    [Fact]
    public async Task Redis_backplane_delivers_public_and_private_events_across_hosts_with_increasing_versions()
    {
        var first = replicas.First;
        var second = replicas.Second;
        var (ownerHttp, ownerId) = await second.SignUpAsync($"replica-live-{Guid.NewGuid():N}@yopmail.com");
        var title = "Cross-host live " + Guid.NewGuid().ToString("N");
        using var created = await second.SendAsync(ownerHttp, "/posts", new { title, body = "Body" });
        var id = await first.PostIdAsync(title);
        await using var guest = await RealtimeTests.LiveConnection.Open(second.RealtimeClient());
        using var ownerRealtime = await second.RealtimeClientAsync(ownerHttp, ownerId);
        await using var owner = await RealtimeTests.LiveConnection.Open(ownerRealtime);
        var publisher = first.Services.GetRequiredService<RealtimeEvents>();
        await publisher.ChangedAsync(id, "another-user", "Cross-host notification", published: true);
        var publicEvents = await guest.ReadUntil("NewPost");
        var privateEvents = await owner.ReadUntil("AnalyticsChanged");
        Assert.Contains("Cross-host notification", privateEvents);
        Assert.DoesNotContain("Notification", publicEvents);
        var previous = Sequence(publicEvents);
        await second.Services.GetRequiredService<RealtimeEvents>().ChangedAsync(id);
        var next = Sequence(await guest.ReadUntil("PostChanged"));
        Assert.True(next > previous, $"{next} must be larger than {previous}");
        // A new publisher object (as after a process restart) cannot reset the shared version.
        var restarted = ActivatorUtilities.CreateInstance<RealtimeEvents>(first.Services);
        await restarted.ChangedAsync(id);
        Assert.True(Sequence(await guest.ReadUntil("PostChanged")) > next);
        await restarted.RemovedAsync(id, "unused-owner", true);
        Assert.Contains("PostRemoved", await guest.ReadUntil("PostRemoved"));
    }

    [Fact]
    public async Task Parallel_limiters_share_a_budget_and_recreation_cannot_reset_it()
    {
        var key = replicas.Namespace + ":limit:" + Guid.NewGuid().ToString("N");
        using var first = new RedisFixedWindowRateLimiter(replicas.First.Services.GetRequiredService<IConnectionMultiplexer>(), key, 10);
        using var second = new RedisFixedWindowRateLimiter(replicas.Second.Services.GetRequiredService<IConnectionMultiplexer>(), key, 10);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i => await (i % 2 == 0 ? first : second).AcquireAsync()));
        Assert.Equal(10, attempts.Count(lease => lease.IsAcquired));
        using var recreated = new RedisFixedWindowRateLimiter(replicas.Second.Services.GetRequiredService<IConnectionMultiplexer>(), key, 10);
        using var denied = await recreated.AcquireAsync();
        Assert.False(denied.IsAcquired);
        Assert.True(denied.TryGetMetadata(MetadataName.RetryAfter, out var retry));
        Assert.InRange(retry.TotalSeconds, 0, 60);
        Assert.InRange(await replicas.First.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyTimeToLiveAsync(key) ?? TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        foreach (var lease in attempts) lease.Dispose();
    }

    static long Sequence(string events)
    {
        using var frame = JsonDocument.Parse(events.Split('\u001e').First(x => x.Contains("PostChanged")));
        return frame.RootElement.GetProperty("arguments")[0].GetProperty("sequence").GetInt64();
    }

    [Fact]
    public async Task Two_workers_claim_each_due_job_only_once()
    {
        var now = DateTime.UtcNow;
        var ids = await replicas.First.QueryAsync(async db =>
        {
            var jobs = Enumerable.Range(0, 120).Select(_ => new Job
            {
                Type = "ReplicaClaimProbe",
                Payload = "{}",
                Status = JobStatus.Pending,
                CreatedAt = now,
                RunAt = now.AddSeconds(30),
            }).ToArray();
            db.Jobs.AddRange(jobs);
            await db.SaveChangesAsync();
            return jobs.Select(j => j.Id).ToHashSet();
        });
        // Only these manual workers see the probe rows as due, so hosted workers cannot race the assertion.
        var clock = new FutureClock(now.AddMinutes(1));
        var first = ActivatorUtilities.CreateInstance<JobWorker>(replicas.First.WorkerServices, clock);
        var second = ActivatorUtilities.CreateInstance<JobWorker>(replicas.Second.WorkerServices, clock);
        var claimed = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var batches = await Task.WhenAll(first.ClaimBatchAsync(default), second.ClaimBatchAsync(default));
            claimed.AddRange(batches.SelectMany(batch => batch));
        }
        Assert.Equal(claimed.Count, claimed.Distinct().Count());
        Assert.True(ids.IsSubsetOf(claimed));
        Assert.Equal(120, await replicas.First.QueryAsync(db => db.Jobs.CountAsync(j => ids.Contains(j.Id) && j.Status == JobStatus.Running)));
    }

    [Fact]
    public async Task Two_schedulers_execute_a_due_occurrence_once_while_its_lease_is_held()
    {
        var name = "replica-cron-probe";
        await replicas.First.QueryAsync(async db =>
        {
            db.CronLeases.Add(new CronLease { Name = name, LastRunAt = DateTime.UtcNow.AddMinutes(-2) });
            await db.SaveChangesAsync();
            return true;
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        async Task Run(IServiceProvider _, CancellationToken ct)
        {
            Interlocked.Increment(ref runs);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        }
        var entry = (Name: name, Cron: "0 * * * * *", Run: (Func<IServiceProvider, CancellationToken, Task>)Run);
        var expression = CrontabSchedule.Parse(entry.Cron, new CrontabSchedule.ParseOptions { IncludingSeconds = true });
        var first = ActivatorUtilities.CreateInstance<CronJobs>(replicas.First.WorkerServices);
        var second = ActivatorUtilities.CreateInstance<CronJobs>(replicas.Second.WorkerServices);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = Task.WhenAll(first.Tick(entry, expression, timeout.Token), second.Tick(entry, expression, timeout.Token));
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.Equal(1, Volatile.Read(ref runs));
        }
        finally { release.TrySetResult(); }
        await pending;
        await second.Tick(entry, expression, timeout.Token);
        Assert.Equal(1, runs);
    }

    sealed class FutureClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
