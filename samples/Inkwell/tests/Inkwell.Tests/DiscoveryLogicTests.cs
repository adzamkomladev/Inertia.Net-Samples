using Inkwell.Features.Discovery;
using Inkwell.Infrastructure;
using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Inkwell.Tests;

public sealed class DiscoveryLogicTests
{
    static readonly DateTimeOffset Now = new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Profile_caps_each_post_action_and_uses_like_comment_view_weights()
    {
        var posts = new Dictionary<string, DiscoveryPost>(StringComparer.Ordinal)
        {
            ["liked"] = Post("liked", "author-like", "topic-like"),
            ["commented"] = Post("commented", "author-comment", "topic-comment"),
            ["viewed"] = Post("viewed", "author-view", "topic-view"),
            ["old"] = Post("old", "author-old", "topic-old"),
        };
        var signals = new[]
        {
            Signal("user", "liked", DiscoverySignalKind.Like, Now),
            Signal("user", "liked", DiscoverySignalKind.Like, Now.AddMinutes(-1)),
            Signal("user", "commented", DiscoverySignalKind.Comment, Now),
            Signal("user", "viewed", DiscoverySignalKind.View, Now),
            Signal("user", "old", DiscoverySignalKind.Like, Now.AddDays(-91)),
            Signal("other-user", "old", DiscoverySignalKind.Like, Now),
        };

        var profile = RecommendationScoring.BuildProfile("user", signals, posts, Now);

        Assert.Equal(4d / 7d, profile.AuthorWeights["author-like"], 6);
        Assert.Equal(2d / 7d, profile.AuthorWeights["author-comment"], 6);
        Assert.Equal(1d / 7d, profile.AuthorWeights["author-view"], 6);
        Assert.DoesNotContain("author-old", profile.AuthorWeights.Keys);
        Assert.Equal(1d, profile.TopicWeights.Values.Sum(), 8);
    }

    [Fact]
    public void Profile_uses_fourteen_day_half_life_and_stable_top_twenty()
    {
        var posts = new Dictionary<string, DiscoveryPost>(StringComparer.Ordinal);
        var signals = new List<DiscoverySignal>();
        for (var i = 0; i < 22; i++)
        {
            var id = $"{i:00}";
            posts[id] = Post(id, $"author-{id}", $"topic-{id}");
            signals.Add(Signal("user", id, DiscoverySignalKind.Like, Now.AddDays(-14)));
        }
        posts["fresh"] = Post("fresh", "author-fresh", "topic-fresh");
        signals.Add(Signal("user", "fresh", DiscoverySignalKind.Like, Now));

        var profile = RecommendationScoring.BuildProfile("user", signals, posts, Now);

        Assert.Equal(20, profile.AuthorWeights.Count);
        Assert.Contains("author-00", profile.AuthorWeights.Keys);
        Assert.Contains("author-18", profile.AuthorWeights.Keys);
        Assert.DoesNotContain("author-19", profile.AuthorWeights.Keys);
        Assert.Contains("author-fresh", profile.AuthorWeights.Keys);
        // A fresh equal-action signal is twice the raw weight of a signal exactly one half-life old.
        Assert.Equal(0.5, profile.AuthorWeights["author-00"] / profile.AuthorWeights["author-fresh"], 6);
    }

    [Fact]
    public void For_you_and_following_interests_use_independent_profiles()
    {
        var posts = new Dictionary<string, DiscoveryPost>(StringComparer.Ordinal)
        {
            ["own-interest"] = Post("own-interest", "author-own", "topic-own"),
            ["followed-interest"] = Post("followed-interest", "author-followed", "topic-followed"),
        };
        var signals = new[]
        {
            Signal("viewer", "own-interest", DiscoverySignalKind.Like, Now),
            Signal("followed-user-a", "followed-interest", DiscoverySignalKind.Like, Now),
            Signal("followed-user-b", "followed-interest", DiscoverySignalKind.View, Now),
        };
        var ownProfile = RecommendationScoring.BuildProfile("viewer", signals, posts, Now);
        var followedMean = RecommendationScoring.MeanNormalizedProfiles(new[]
        {
            RecommendationScoring.BuildProfile("followed-user-a", signals, posts, Now),
            RecommendationScoring.BuildProfile("followed-user-b", signals, posts, Now),
        });
        var candidate = Post("candidate", "author-followed", "topic-followed");

        // If the own vector leaked into Following, or the followed mean leaked into For you, these
        // zero-score assertions fail. Callers choose one vector for a feed; the scorer never blends.
        Assert.Equal(0d, RecommendationScoring.Score(candidate, ownProfile, Now));
        Assert.Equal(0d, RecommendationScoring.Score(candidate with { TopicIds = ["topic-own"], AuthorId = "author-own" }, followedMean, Now));
        Assert.Equal(1d, RecommendationScoring.Score(candidate, followedMean, Now), 8);
    }

    [Fact]
    public void Post_score_uses_seventy_thirty_topic_author_weights_and_recency()
    {
        var profile = new DiscoveryProfile(
            new Dictionary<string, double>(StringComparer.Ordinal) { ["topic"] = 1 },
            new Dictionary<string, double>(StringComparer.Ordinal) { ["author"] = 1 });
        var topicOnly = Post("topic-only", "other", "topic");
        var authorOnly = Post("author-only", "author", "other-topic");

        Assert.Equal(0.7, RecommendationScoring.ProfileScore(topicOnly, profile), 8);
        Assert.Equal(0.3, RecommendationScoring.ProfileScore(authorOnly, profile), 8);
        Assert.Equal(0.75, RecommendationScoring.RecencyFactor(Now.AddDays(-14), Now), 8);
        Assert.Equal(1, RecommendationScoring.RecencyFactor(Now.AddDays(1), Now), 8);
        Assert.Equal(0.5, RecommendationScoring.RecencyFactor(Now.AddDays(-40000), Now), 8);
    }

    [Fact]
    public void Ranking_excludes_archived_and_own_posts_and_applies_minimum_score()
    {
        var profile = new DiscoveryProfile(
            new Dictionary<string, double>(StringComparer.Ordinal) { ["topic"] = 1 },
            new Dictionary<string, double>(StringComparer.Ordinal));
        var candidates = new[]
        {
            Post("match", "other", "topic"),
            Post("own", "viewer", "topic"),
            Post("archived", "other", "topic") with { IsArchived = true },
            Post("weak", "other", "weak-topic"),
        };
        var weakProfile = new DiscoveryProfile(
            new Dictionary<string, double>(StringComparer.Ordinal) { ["weak-topic"] = 0.01 },
            new Dictionary<string, double>(StringComparer.Ordinal));

        var ranked = RecommendationScoring.Rank(candidates, "viewer", profile, Now);

        Assert.Equal(new[] { "match" }, ranked.Select(item => item.Post.Id));
        Assert.Equal(0d, RecommendationScoring.Score(candidates[3], weakProfile, Now));
    }

    [Fact]
    public void Digest_clock_uses_injected_time_and_requires_local_eight_am()
    {
        var before = new DigestClock(new FixedTimeProvider(new DateTimeOffset(2026, 1, 2, 7, 59, 59, TimeSpan.Zero)));
        var due = new DigestClock(new FixedTimeProvider(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero)));

        Assert.False(before.TryGetDueWindow("UTC", out _));
        Assert.True(due.TryGetDueWindow("UTC", out var window));
        Assert.Equal(new DateOnly(2026, 1, 1), window.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), window.EndUtc);
    }

    [Fact]
    public void Digest_clock_creates_twenty_three_and_twenty_five_hour_local_days()
    {
        Assert.True(DigestClock.TryGetDueWindow(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero), "America/New_York", out var spring));
        Assert.Equal(new DateOnly(2026, 3, 8), spring.LocalDate);
        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);

        Assert.True(DigestClock.TryGetDueWindow(new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero), "America/New_York", out var fall));
        Assert.Equal(new DateOnly(2026, 11, 1), fall.LocalDate);
        Assert.Equal(TimeSpan.FromHours(25), fall.EndUtc - fall.StartUtc);
    }

    [Fact]
    public void Next_digest_due_uses_local_eight_and_advances_after_the_daily_send_time()
    {
        var before = DigestClock.NextDueUtc("America/New_York", new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero));
        var after = DigestClock.NextDueUtc("America/New_York", new DateTimeOffset(2026, 1, 2, 13, 0, 0, TimeSpan.Zero));
        var spring = DigestClock.NextDueUtc("America/New_York", new DateTimeOffset(2026, 3, 8, 11, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 13, 0, 0, TimeSpan.Zero), before);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 13, 0, 0, TimeSpan.Zero), after);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero), spring);
    }

    [Fact]
    public void Local_day_boundary_shifts_through_invalid_midnight_and_chooses_earlier_ambiguous_instant()
    {
        var zone = CreateMidnightTransitionZone();
        var skippedMidnight = DigestClock.GetLocalDayBoundaryUtc(new DateOnly(2026, 3, 8), zone);
        var repeatedMidnight = DigestClock.GetLocalDayBoundaryUtc(new DateOnly(2026, 11, 1), zone);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), skippedMidnight);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), repeatedMidnight);
    }

    [Fact]
    public void Time_zone_validation_accepts_Iana_ids_and_rejects_display_names()
    {
        Assert.True(DigestClock.IsValidIanaTimeZone("UTC"));
        Assert.True(DigestClock.IsValidIanaTimeZone("America/New_York"));
        Assert.False(DigestClock.IsValidIanaTimeZone("Eastern Standard Time"));
        Assert.False(DigestClock.IsValidIanaTimeZone("Not/A_Zone"));
    }

    [Fact]
    public void Optional_backend_names_and_redis_url_are_scoped_and_redact_credentials()
    {
        var names = new OptionalServiceNamespace("Inkwell", "Development");
        var redis = RedisUrlSettings.Parse("rediss://default:p%40ss@cache.example:6380/4");

        Assert.Equal("Inkwell:Development:feed:user-1", names.RedisKey("feed:user-1"));
        Assert.Equal("Inkwell-Development-posts", names.MeilisearchIndex("posts"));
        Assert.Equal("cache.example", redis.Host);
        Assert.Equal(6380, redis.Port);
        Assert.Equal(4, redis.Database);
        Assert.True(redis.UseTls);
        Assert.Equal("default", redis.Username);
        Assert.Equal("p@ss", redis.Password);
        Assert.DoesNotContain("p@ss", redis.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Optional_read_cache_fails_open_and_namespaces_keys()
    {
        var backend = new FailingDistributedCache();
        var cache = new OptionalReadCache(backend, new OptionalServiceNamespace("Inkwell", "test"));
        var result = await cache.GetOrCreateAsync("feed:user-1", () => Task.FromResult(new[] { 3L, 4L }));

        Assert.Equal(new long[] { 3, 4 }, result);
        Assert.Equal("Inkwell:test:feed:user-1", backend.LastKey);
    }

    [Fact]
    public async Task Meilisearch_enqueue_returns_persistable_task_id_and_waits_for_success()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
                return Json(HttpStatusCode.Accepted, """{"taskUid":42,"status":"enqueued"}""");
            return Json(HttpStatusCode.OK, """{"uid":42,"status":"succeeded"}""");
        });
        using var http = new HttpClient(handler);
        var settings = new MeilisearchConnectionSettings(new Uri("https://search.example/"), "index-secret");
        var client = new MeilisearchTaskClient(http, settings);

        var taskId = await client.EnqueueUpsertAsync("Inkwell-Development-posts", new[] { new { id = "p-1", title = "Hello" } });
        var result = await client.WaitForSuccessAsync(taskId, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1));

        Assert.Equal(42, taskId.Value);
        Assert.Equal(MeilisearchTaskState.Succeeded, result.State);
        Assert.Equal("/indexes/Inkwell-Development-posts/documents", handler.Paths[0]);
        Assert.Equal("/tasks/42", handler.Paths[1]);
        Assert.All(handler.Authorization, value => Assert.Equal("Bearer index-secret", value));
        Assert.DoesNotContain("index-secret", settings.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Meilisearch_index_creation_checks_then_posts_index_definition()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/indexes/Inkwell-Development-posts" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/indexes" => Json(HttpStatusCode.Accepted, """{"taskUid":77,"status":"enqueued"}"""),
            "/tasks/77" => Json(HttpStatusCode.OK, """{"uid":77,"status":"succeeded"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var http = new HttpClient(handler);
        var client = new MeilisearchTaskClient(http, new MeilisearchConnectionSettings(new Uri("https://search.example/"), null));

        var task = await client.EnsureIndexAsync("Inkwell-Development-posts", "id");
        await client.WaitForSuccessAsync(task!.Value, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Get }, handler.Methods);
        Assert.Equal(new[] { "/indexes/Inkwell-Development-posts", "/indexes", "/tasks/77" }, handler.Paths);
        Assert.Contains("\"uid\":\"Inkwell-Development-posts\"", handler.Bodies[1]);
        Assert.Contains("\"primaryKey\":\"id\"", handler.Bodies[1]);
    }

    [Fact]
    public async Task Meilisearch_index_creation_treats_an_existing_index_as_success()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var http = new HttpClient(handler);
        var client = new MeilisearchTaskClient(http, new MeilisearchConnectionSettings(new Uri("https://search.example/"), null));

        var task = await client.EnsureIndexAsync("Inkwell-Development-posts");

        Assert.Null(task);
        Assert.Equal(new[] { HttpMethod.Get }, handler.Methods);
    }

    [Fact]
    public async Task Index_creation_race_requires_compatible_existing_index()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/tasks/44"
            ? Json(HttpStatusCode.OK, "{\"uid\":44,\"status\":\"failed\",\"error\":{\"code\":\"index_already_exists\"}}")
            : Json(HttpStatusCode.OK, "{\"uid\":\"sample-test-posts\",\"primaryKey\":\"id\"}"));
        using var http = new HttpClient(handler);
        var client = new MeilisearchTaskClient(http, new MeilisearchConnectionSettings(new Uri("https://search.example/"), "test"));
        await client.ConfirmIndexCreationAsync(new MeilisearchTaskId(44), "sample-test-posts");
        Assert.Equal(new[] { "/tasks/44", "/indexes/sample-test-posts" }, handler.Paths);
    }

    static DiscoveryPost Post(string id, string author, string topic) =>
        new(id, author, [topic], Now);

    static DiscoverySignal Signal(string user, string post, DiscoverySignalKind kind, DateTimeOffset occurredAt) =>
        new(user, post, kind, occurredAt);

    static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    static TimeZoneInfo CreateMidnightTransitionZone()
    {
        var spring = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 0, 0, 0), 3, 2, DayOfWeek.Sunday);
        var fall = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 1, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2025, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), spring, fall);
        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/LocalMidnightTransitions", TimeSpan.FromHours(-5), "Midnight transition test", "Standard", "Daylight", [rule]);
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string?> Authorization { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Authorization.Add(request.Headers.Authorization?.ToString());
            Methods.Add(request.Method);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return createResponse(request);
        }
    }

    sealed class FailingDistributedCache : IDistributedCache
    {
        public string? LastKey { get; private set; }
        public byte[]? Get(string key) => throw new InvalidOperationException("cache offline");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            LastKey = key;
            throw new InvalidOperationException("cache offline");
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }
}
