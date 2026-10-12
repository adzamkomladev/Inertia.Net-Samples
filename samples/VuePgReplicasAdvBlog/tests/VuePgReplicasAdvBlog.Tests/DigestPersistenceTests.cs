using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Digests;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Email;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Tests;

public sealed class DigestPersistenceTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Due_digest_persists_local_dates_expiry_and_only_unseen_personal_matches_once()
    {
        var now = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero); // 08:00 after New York's spring DST change
        var ids = await CreateDigestDataAsync(now, includeSeenCandidate: true);
        var clock = new FixedTimeProvider(now);
        var discovery = new DiscoveryService(clock);
        var digest = new DigestService(discovery, new DigestClock(clock), factory.Services.GetRequiredService<IDataProtectionProvider>());
        var jobs = new JobQueue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        Assert.Equal(1, await digest.EnqueueDueAsync(db, jobs));
        var delivery = await db.DigestDeliveries.AsNoTracking().SingleAsync(row => row.UserId == ids.UserId);

        Assert.Equal(new DateOnly(2026, 3, 9), delivery.LocalDate); // send date, with a unique row per local day
        Assert.Equal("America/New_York", delivery.TimeZoneId);
        Assert.Equal(new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc), delivery.WindowStartUtc);
        Assert.Equal(new DateTime(2026, 3, 9, 4, 0, 0, DateTimeKind.Utc), delivery.WindowEndUtc); // yesterday was 23 hours
        Assert.Equal(new DateTime(2026, 3, 10, 4, 0, 0, DateTimeKind.Utc), delivery.ExpiresAtUtc); // end of local send day
        var selected = JsonSerializer.Deserialize<Guid[]>(delivery.PostIdsJson)!;
        Assert.Equal(new[] { ids.UnseenCandidateId }, selected);
        Assert.Equal("Queued", delivery.Status);
        Assert.Equal(1, await db.Jobs.CountAsync(job => job.Type == nameof(SendRecommendationDigest)));

        var user = await db.Users.SingleAsync(row => row.Id == ids.UserId);
        user.NextDigestAtUtc = now.UtcDateTime; // simulate scheduler retry during the same local send day
        await db.SaveChangesAsync();
        Assert.Equal(0, await digest.EnqueueDueAsync(db, jobs));
        Assert.Equal(1, await db.DigestDeliveries.CountAsync(row => row.UserId == ids.UserId));
        Assert.Equal(1, await db.Jobs.CountAsync(job => job.Type == nameof(SendRecommendationDigest)));
    }

    [Fact]
    public async Task Expired_queued_delivery_is_skipped_and_expired_sending_delivery_is_unknown()
    {
        var now = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var ids = await CreateDigestDataAsync(now, includeSeenCandidate: false, dailyOptIn: false);
        Guid queuedId;
        Guid sendingId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var expired = now.AddMinutes(-1).UtcDateTime;
            db.DigestDeliveries.AddRange(
                new DigestDelivery
                {
                    UserId = ids.UserId,
                    LocalDate = new DateOnly(2026, 3, 8),
                    TimeZoneId = "America/New_York",
                    WindowStartUtc = expired.AddDays(-1),
                    WindowEndUtc = expired,
                    ExpiresAtUtc = expired,
                    PostIdsJson = "[]",
                    Status = "Queued"
                },
                new DigestDelivery
                {
                    UserId = ids.UserId,
                    LocalDate = new DateOnly(2026, 3, 7),
                    TimeZoneId = "America/New_York",
                    WindowStartUtc = expired.AddDays(-1),
                    WindowEndUtc = expired,
                    ExpiresAtUtc = expired,
                    PostIdsJson = "[]",
                    Status = "Sending"
                });
            await db.SaveChangesAsync();
            queuedId = await db.DigestDeliveries.Where(row => row.UserId == ids.UserId && row.Status == "Queued").Select(row => row.Id).SingleAsync();
            sendingId = await db.DigestDeliveries.Where(row => row.UserId == ids.UserId && row.Status == "Sending").Select(row => row.Id).SingleAsync();
        }

        var clock = new FixedTimeProvider(now);
        var digest = new DigestService(new DiscoveryService(clock), new DigestClock(clock), factory.Services.GetRequiredService<IDataProtectionProvider>());
        var mailer = factory.WorkerServices.GetRequiredService<Mailer>();
        await using var deliveryScope = factory.Services.CreateAsyncScope();
        var deliveryDb = deliveryScope.ServiceProvider.GetRequiredService<AppDb>();
        await digest.SendAsync(deliveryDb, mailer, queuedId, "", "", "https://example.test");
        await digest.SendAsync(deliveryDb, mailer, sendingId, "", "", "https://example.test");

        Assert.Equal("Skipped", await deliveryDb.DigestDeliveries.Where(row => row.Id == queuedId).Select(row => row.Status).SingleAsync());
        Assert.Equal("Unknown", await deliveryDb.DigestDeliveries.Where(row => row.Id == sendingId).Select(row => row.Status).SingleAsync());
    }

    async Task<(Guid UserId, Guid UnseenCandidateId)> CreateDigestDataAsync(
        DateTimeOffset now,
        bool includeSeenCandidate,
        bool dailyOptIn = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var user = new User
        {
            Email = $"digest-{Guid.NewGuid():N}@example.com",
            Name = "Digest Reader",
            EmailVerifiedAt = now.UtcDateTime,
            NextDigestAtUtc = now.UtcDateTime,
            Preferences = new UserPreferences { DailyRecommendationDigest = dailyOptIn, TimeZoneId = "America/New_York" },
        };
        var signalAuthor = NewUser("signal-author");
        var seenAuthor = NewUser("seen-author");
        var unseenAuthor = NewUser("unseen-author");
        var unrelatedAuthor = NewUser("unrelated-author");
        db.Users.AddRange(user, signalAuthor, seenAuthor, unseenAuthor, unrelatedAuthor);
        await db.SaveChangesAsync();
        var signal = NewPost(signalAuthor.Id, "Signal", now.AddDays(-2));
        var unseen = NewPost(unseenAuthor.Id, "Unseen candidate", new DateTimeOffset(2026, 3, 8, 15, 0, 0, TimeSpan.Zero));
        var unrelated = NewPost(unrelatedAuthor.Id, "No affinity", new DateTimeOffset(2026, 3, 8, 16, 0, 0, TimeSpan.Zero));
        var seen = NewPost(seenAuthor.Id, "Already seen", new DateTimeOffset(2026, 3, 8, 17, 0, 0, TimeSpan.Zero));
        db.Posts.AddRange(signal, unseen, unrelated, seen);
        await db.SaveChangesAsync();
        var topic = $"t-{Guid.NewGuid():N}"[..24];
        db.PostTopics.AddRange(new PostTopic { PostId = signal.Id, Topic = topic }, new PostTopic { PostId = unseen.Id, Topic = topic },
            new PostTopic { PostId = seen.Id, Topic = topic });
        db.Likes.Add(new Like { UserId = user.Id, PostId = signal.Id, CreatedAt = now.AddDays(-1).UtcDateTime });
        if (includeSeenCandidate)
        {
            var seenAt = new DateTime(2026, 3, 8, 18, 0, 0, DateTimeKind.Utc);
            db.SeenPosts.Add(new SeenPost { UserId = user.Id, PostId = seen.Id, FirstSeenAt = seenAt, LastSeenAt = seenAt });
        }
        await db.SaveChangesAsync();
        return (user.Id, unseen.Id);
    }

    static User NewUser(string name) => new() { Email = $"{name}-{Guid.NewGuid():N}@example.com", Name = name, EmailVerifiedAt = DateTime.UtcNow };

    static Post NewPost(Guid authorId, string title, DateTimeOffset published) => new()
    {
        AuthorId = authorId,
        Title = title,
        Slug = $"test-{Guid.NewGuid():N}"[..13],
        Body = "Post body",
        Excerpt = "Post body",
        CreatedAt = published.UtcDateTime,
        UpdatedAt = published.UtcDateTime,
        PublishedAt = published.UtcDateTime,
    };

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
