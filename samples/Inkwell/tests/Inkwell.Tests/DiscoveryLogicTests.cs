using Inkwell.Features.Discovery;
using Inkwell.Infrastructure;

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

    static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Digest_window_opens_at_local_eight_am_and_covers_the_previous_local_day()
    {
        Assert.False(DigestSchedule.TryGetDueWindow(Utc(2026, 1, 2, 7).AddSeconds(3599), "UTC", out _));
        Assert.True(DigestSchedule.TryGetDueWindow(Utc(2026, 1, 2, 8), "UTC", out var window));
        Assert.Equal(new DateOnly(2026, 1, 1), window.LocalDate);
        Assert.Equal(Utc(2026, 1, 1), window.StartUtc);
        Assert.Equal(Utc(2026, 1, 2), window.EndUtc);
    }

    [Fact]
    public void Digest_window_is_twenty_three_or_twenty_five_hours_across_dst_changes()
    {
        Assert.True(DigestSchedule.TryGetDueWindow(Utc(2026, 3, 9, 12), "America/New_York", out var spring));
        Assert.Equal(new DateOnly(2026, 3, 8), spring.LocalDate);
        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);

        Assert.True(DigestSchedule.TryGetDueWindow(Utc(2026, 11, 2, 14), "America/New_York", out var fall));
        Assert.Equal(new DateOnly(2026, 11, 1), fall.LocalDate);
        Assert.Equal(TimeSpan.FromHours(25), fall.EndUtc - fall.StartUtc);
    }

    [Fact]
    public void Next_digest_is_the_coming_local_eight_am()
    {
        Assert.Equal(Utc(2026, 1, 2, 13), DigestSchedule.NextDueUtc("America/New_York", Utc(2026, 1, 2, 12)));
        Assert.Equal(Utc(2026, 1, 3, 13), DigestSchedule.NextDueUtc("America/New_York", Utc(2026, 1, 2, 13)));
        Assert.Equal(Utc(2026, 3, 8, 12), DigestSchedule.NextDueUtc("America/New_York", Utc(2026, 3, 8, 11)));
    }

    [Fact]
    public void Time_zone_validation_accepts_Iana_ids_and_rejects_display_names()
    {
        Assert.True(DigestSchedule.IsValidZone("UTC"));
        Assert.True(DigestSchedule.IsValidZone("America/New_York"));
        Assert.False(DigestSchedule.IsValidZone("Eastern Standard Time"));
        Assert.False(DigestSchedule.IsValidZone("Not/A_Zone"));
    }

    static DiscoveryPost Post(string id, string author, string topic) =>
        new(id, author, [topic], Now);

    static DiscoverySignal Signal(string user, string post, DiscoverySignalKind kind, DateTimeOffset occurredAt) =>
        new(user, post, kind, occurredAt);
}
