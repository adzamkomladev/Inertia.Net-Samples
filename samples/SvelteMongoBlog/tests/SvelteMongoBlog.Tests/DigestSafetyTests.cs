using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Discovery;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Tests;

public sealed class DigestSafetyTests(DigestTestAppFactory app) : IClassFixture<DigestTestAppFactory>
{
    [Fact]
    public async Task Concurrent_scheduler_runs_create_one_delivery_and_one_job_for_the_local_day()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var userId = await SeedDigestDataAsync(db, now);
        var discovery = new DiscoveryService(clock);
        await discovery.RefreshProfileAsync(db, userId);
        var digest = NewDigest(app.Services, discovery, clock);
        var jobs = new JobQueue();

        _ = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => digest.EnqueueDueAsync(db, jobs)));
        Assert.Equal(1, await db.DigestDeliveries.CountDocumentsAsync(x => x.UserId == userId && x.LocalDate == new DateOnly(2026, 10, 7)));
        var delivery = await db.DigestDeliveries.Find(x => x.UserId == userId).SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 7), delivery.LocalDate);
        Assert.Equal("Queued", delivery.Status);
        var jobsForDelivery = await db.Jobs.Find(x => x.Type == nameof(SendRecommendationDigest)).Project(x => x.Payload).ToListAsync();
        Assert.Single(jobsForDelivery, payload => payload.Contains($"\"DeliveryId\":\"{delivery.Id}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_send_marks_uncertain_outcome_unknown_and_never_blindly_resends()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var userId = await SeedDigestDataAsync(db, now);
        var discovery = new DiscoveryService(clock);
        await discovery.RefreshProfileAsync(db, userId);
        var jobs = new JobQueue();
        var digest = NewDigest(app.Services, discovery, clock);
        Assert.Equal(1, await digest.EnqueueDueAsync(db, jobs));
        var delivery = await db.DigestDeliveries.Find(x => x.UserId == userId).SingleAsync();
        var user = await db.Users.Find(x => x.Id == userId).SingleAsync();
        var handler = new BlockingUnavailableMailerHandler();
        using var http = new HttpClient(handler);
        var mailer = NewMailer(app.Services, http);

        var firstSend = digest.SendAsync(db, mailer, delivery.Id, user.Email, user.Name, "https://example.test");
        await handler.SendStarted.WaitAsync(TimeSpan.FromSeconds(10));
        await digest.SendAsync(db, mailer, delivery.Id, user.Email, user.Name, "https://example.test");
        handler.Release();
        await firstSend;

        var after = await db.DigestDeliveries.Find(x => x.Id == delivery.Id).SingleAsync();
        Assert.Equal("Unknown", after.Status);
        Assert.Equal(1, after.Attempts);
        Assert.Contains("uncertain", after.LastError ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Count);

        await digest.SendAsync(db, mailer, delivery.Id, user.Email, user.Name, "https://example.test");
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Expired_queued_delivery_is_skipped_and_expired_inflight_delivery_is_unknown()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var userId = Guid.CreateVersion7();
        await db.Users.InsertOneAsync(new User
        {
            Id = userId, Email = $"digest-expired-{Guid.NewGuid():N}@yopmail.com", Name = "Expired reader",
            EmailVerifiedAt = now.AddDays(-1).UtcDateTime, Preferences = new UserPreferences { DailyRecommendationDigest = true, TimeZoneId = "UTC" },
        });
        var expiredAt = now.AddMinutes(-1).UtcDateTime;
        var queuedId = Guid.CreateVersion7();
        var sendingId = Guid.CreateVersion7();
        await db.DigestDeliveries.InsertManyAsync([
            NewExpiredDelivery(queuedId, userId, new DateOnly(2026, 10, 6), expiredAt, "Queued"),
            NewExpiredDelivery(sendingId, userId, new DateOnly(2026, 10, 5), expiredAt, "Sending"),
        ]);
        var handler = new CountingSuccessMailerHandler();
        using var http = new HttpClient(handler);
        var mailer = NewMailer(app.Services, http);
        var digest = NewDigest(app.Services, new DiscoveryService(clock), clock);

        await digest.SendAsync(db, mailer, queuedId, "", "", "https://example.test");
        await digest.SendAsync(db, mailer, sendingId, "", "", "https://example.test");

        Assert.Equal("Skipped", (await db.DigestDeliveries.Find(x => x.Id == queuedId).SingleAsync()).Status);
        Assert.Equal("Unknown", (await db.DigestDeliveries.Find(x => x.Id == sendingId).SingleAsync()).Status);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Successful_digest_persists_provider_reference_and_demo_mail_is_captured_locally()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var userId = await SeedDigestDataAsync(db, now);
        var discovery = new DiscoveryService(clock);
        await discovery.RefreshProfileAsync(db, userId);
        var digest = NewDigest(app.Services, discovery, clock);
        Assert.Equal(1, await digest.EnqueueDueAsync(db, new JobQueue()));
        var delivery = await db.DigestDeliveries.Find(x => x.UserId == userId).SingleAsync();
        var user = await db.Users.Find(x => x.Id == userId).SingleAsync();
        var success = new CountingSuccessMailerHandler();
        using var http = new HttpClient(success);
        await digest.SendAsync(db, NewMailer(app.Services, http), delivery.Id, user.Email, user.Name, "https://example.test");
        var sent = await db.DigestDeliveries.Find(x => x.Id == delivery.Id).SingleAsync();
        Assert.Equal("Sent", sent.Status);
        Assert.Equal("maileroo-test-reference", sent.ProviderReference);
        Assert.Equal(1, success.Count);

        var captureRoot = Path.Combine(Path.GetTempPath(), $"svelte-captured-mail-{Guid.NewGuid():N}");
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maileroo:ApiKey"] = "fake-configured-key",
                ["Maileroo:From"] = "noreply@example.test",
                ["Maileroo:FromName"] = "Test",
                ["App:DataDir"] = captureRoot,
            }).Build();
            var demoHandler = new CountingSuccessMailerHandler();
            using var demoHttp = new HttpClient(demoHandler);
            var demoMailer = new Mailer(demoHttp, config, app.Services.GetRequiredService<IHostEnvironment>(), NullLogger<Mailer>.Instance);
            var reference = await demoMailer.SendAsync("inertia.demo42.00001@yopmail.com", "Welcome", "<p>Captured</p>", "Captured locally", CancellationToken.None);
            var files = Directory.GetFiles(Path.Combine(captureRoot, "captured-mail"), "*.txt");
            Assert.Null(reference);
            Assert.Equal(0, demoHandler.Count);
            Assert.Single(files);
            Assert.Contains("inertia.demo42.00001@yopmail.com", await File.ReadAllTextAsync(files[0]));
        }
        finally
        {
            if (Directory.Exists(captureRoot)) Directory.Delete(captureRoot, recursive: true);
        }
    }

    static DigestService NewDigest(IServiceProvider services, DiscoveryService discovery, TimeProvider clock) =>
        new(discovery, new DigestClock(clock), services.GetRequiredService<IDataProtectionProvider>());

    static Mailer NewMailer(IServiceProvider services, HttpClient http)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maileroo:ApiKey"] = "test-api-key",
            ["Maileroo:From"] = "noreply@example.test",
            ["Maileroo:FromName"] = "Test",
        }).Build();
        return new Mailer(http, config, services.GetRequiredService<IHostEnvironment>(), NullLogger<Mailer>.Instance);
    }

    static async Task<Guid> SeedDigestDataAsync(AppDb db, DateTimeOffset now)
    {
        var userId = Guid.CreateVersion7();
        var signalAuthorId = Guid.CreateVersion7();
        var candidateAuthorId = Guid.CreateVersion7();
        var reader = new User
        {
            Id = userId, Email = $"digest-safety-{Guid.NewGuid():N}@yopmail.com", Name = "Reader",
            EmailVerifiedAt = now.AddDays(-30).UtcDateTime, NextDigestAtUtc = now.AddMinutes(-1).UtcDateTime,
            Preferences = new UserPreferences { DailyRecommendationDigest = true, TimeZoneId = "UTC" },
        };
        var signalAuthor = new User { Id = signalAuthorId, Email = $"digest-signal-{Guid.NewGuid():N}@example.test", Name = "Signal author" };
        var candidateAuthor = new User { Id = candidateAuthorId, Email = $"digest-candidate-{Guid.NewGuid():N}@example.test", Name = "Candidate author" };
        await db.Users.InsertManyAsync([reader, signalAuthor, candidateAuthor]);

        var topic = $"digest-{Guid.NewGuid():N}";
        var signalAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var candidateAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var signalId = Guid.CreateVersion7();
        var candidateId = Guid.CreateVersion7();
        await db.Posts.InsertManyAsync([
            NewPost(signalId, signalAuthorId, "Recommendation signal", signalAt),
            NewPost(candidateId, candidateAuthorId, "Yesterday recommendation", candidateAt),
        ]);
        await db.PostTopics.InsertManyAsync([
            new PostTopic { PostId = signalId, Topic = topic },
            new PostTopic { PostId = candidateId, Topic = topic },
        ]);
        await db.Likes.InsertOneAsync(new Like { UserId = userId, PostId = signalId, CreatedAt = now.AddHours(-2).UtcDateTime });
        return userId;
    }

    static Post NewPost(Guid id, Guid authorId, string title, DateTime publishedAt) => new()
    {
        Id = id, AuthorId = authorId, Title = title, Slug = $"{title.Replace(' ', '-').ToLowerInvariant()}-{Guid.NewGuid():N}",
        Body = "Body", Excerpt = title, CreatedAt = publishedAt, UpdatedAt = publishedAt, PublishedAt = publishedAt,
    };

    static DigestDelivery NewExpiredDelivery(Guid id, Guid userId, DateOnly localDate, DateTime expiredAt, string status) => new()
    {
        Id = id, UserId = userId, LocalDate = localDate, TimeZoneId = "UTC",
        WindowStartUtc = expiredAt.AddDays(-1), WindowEndUtc = expiredAt, ExpiresAtUtc = expiredAt,
        PostIdsJson = "[]", Status = status, CreatedAt = expiredAt.AddDays(-1),
    };

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class BlockingUnavailableMailerHandler : HttpMessageHandler
    {
        readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int count;
        public Task SendStarted => started.Task;
        public int Count => Volatile.Read(ref count);
        public void Release() => release.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }

    sealed class CountingSuccessMailerHandler : HttpMessageHandler
    {
        int count;
        public int Count => Volatile.Read(ref count);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new { message_id = "maileroo-test-reference" }),
            });
        }
    }
}

public sealed class DigestTestAppFactory : AppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && (descriptor.ImplementationType?.Name is "JobWorker" or "ViewEventWriter"
                    || descriptor.ImplementationType?.Name.Contains("Ssr", StringComparison.Ordinal) == true)).ToList())
                services.Remove(descriptor);
        });
    }
}
