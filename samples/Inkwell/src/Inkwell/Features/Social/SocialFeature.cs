using System.Security.Claims;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Social;

public static class SocialFeature
{
    static readonly string[] Toggle = ["PUT", "DELETE"];

    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        void Map(string pattern, Delegate handler, bool verified = true)
        {
            var route = app.MapMethods(pattern, Toggle, handler).RequireRateLimiting(RateLimits.Writes);
            if (verified) route.RequireAuthorization(AuthPolicies.Verified);
            else route.RequireAuthorization();
        }

        Map("/posts/{id:guid}/like", (Guid id, HttpContext ctx, SocialService social, CancellationToken ct) =>
            Respond(social.SetPostLikeAsync(id, ctx.User.UserId()!.Value, IsPut(ctx), ct), ctx));
        Map("/posts/{id:guid}/bookmark", (Guid id, HttpContext ctx, SocialService social, CancellationToken ct) =>
            Respond(social.SetBookmarkAsync(id, ctx.User.UserId()!.Value, IsPut(ctx), ct), ctx), verified: false);
        Map("/posts/{id:guid}/repost", (Guid id, HttpContext ctx, SocialService social, CancellationToken ct) =>
            Respond(social.SetRepostAsync(id, ctx.User.UserId()!.Value, IsPut(ctx), ct), ctx));
        Map("/posts/{id:guid}/archive", (Guid id, HttpContext ctx, SocialService social, CancellationToken ct) =>
            Respond(social.SetArchivedAsync(id, ctx.User.UserId()!.Value, IsPut(ctx), ct), ctx), verified: false);
        Map("/comments/{id:guid}/like", (Guid id, HttpContext ctx, SocialService social, CancellationToken ct) =>
            Respond(social.SetCommentLikeAsync(id, ctx.User.UserId()!.Value, IsPut(ctx), ct), ctx));
        Map("/users/{id:guid}/follow", async Task<IResult> (Guid id, HttpContext ctx, SocialService social, CancellationToken ct) =>
            id == ctx.User.UserId() ? Results.BadRequest("You cannot follow yourself.")
                : (await social.SetFollowAsync(id, ctx.User.UserId()!.Value, IsPut(ctx), ct)).Respond(ctx), verified: false);

        app.MapGet("/me/posts", (string? cursor, SocialService social, ClaimsPrincipal user, CancellationToken ct) =>
            Timeline("mine", "My posts", cursor, null, social, user, ct)).RequireAuthorization();
        app.MapGet("/me/archives", (string? cursor, SocialService social, ClaimsPrincipal user, CancellationToken ct) =>
            Timeline("archives", "Archives", cursor, null, social, user, ct)).RequireAuthorization();
        app.MapGet("/bookmarks", (string? cursor, SocialService social, ClaimsPrincipal user, CancellationToken ct) =>
            Timeline("bookmarks", "Bookmarks", cursor, null, social, user, ct)).RequireAuthorization();
        app.MapGet("/feed", RankedFeed).RequireAuthorization();
        app.MapGet("/users", UsersDirectory);
        app.MapGet("/users/{id:guid}", UserProfile);
    }

    static bool IsPut(HttpContext ctx) => HttpMethods.IsPut(ctx.Request.Method);

    static async Task<IResult> Respond(Task<Outcome> outcome, HttpContext ctx) => (await outcome).Respond(ctx);

    static async Task<IResult> RankedFeed(string? mode, string? cursor, SocialService social, ClaimsPrincipal user, CancellationToken ct)
    {
        if (mode is not (null or "for-you" or "following")) return Results.BadRequest("Invalid feed mode.");
        try
        {
            var following = mode == "following";
            var (items, next) = await social.RankedFeedAsync(user.UserId()!.Value,
                following ? DiscoveryFeedMode.FollowingInterests : DiscoveryFeedMode.ForYou, cursor, ct);
            return FeedPage(items, cursor, next, following ? "following" : "for-you", following ? "Following interests" : "For you");
        }
        catch (ArgumentException) { return Results.BadRequest("Invalid feed cursor."); }
        catch (KeyNotFoundException) { return Results.StatusCode(StatusCodes.Status410Gone); }
    }

    static async Task<IResult> UsersDirectory(string? q, string? cursor, SocialService social, ClaimsPrincipal user, CancellationToken ct)
    {
        var page = await social.UsersDirectoryAsync(user.UserId(), q, cursor, ct);
        if (page is null) return Results.BadRequest("Invalid cursor.");
        return Render("Users/Index", new InertiaProps
        {
            ["users"] = Scroll(new CursorPage<UserSummary>(page.Users, cursor, page.Next), "data"),
            ["filters"] = new { q = q ?? "" },
        });
    }

    static async Task<IResult> UserProfile(Guid id, string? cursor, SocialService social, ClaimsPrincipal user, CancellationToken ct)
    {
        var profile = await social.ProfileAsync(user.UserId(), id, ct);
        return profile is null ? Results.NotFound() : await Timeline("user", profile.Name, cursor, profile, social, user, ct);
    }

    static async Task<IResult> Timeline(string mode, string title, string? cursor, UserSummary? profile, SocialService social, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Paging.TryDecode(cursor, out var after)) return Results.BadRequest("Invalid cursor.");
        var page = await social.TimelineAsync(user.UserId(), mode, after, profile, ct);
        if (profile is null) return FeedPage(page.Items, cursor, page.Next, mode, title);
        return Render("Users/Show", new InertiaProps
        {
            ["user"] = profile,
            ["posts"] = Scroll(new CursorPage<PostCardDto>(page.Items, cursor, page.Next), "data"),
        });
    }

    static IResult FeedPage(List<PostCardDto> items, string? cursor, string? next, string mode, string title) =>
        Render("Feed/Index", new InertiaProps
        {
            ["posts"] = Scroll(new CursorPage<PostCardDto>(items, cursor, next), "data"),
            ["mode"] = mode,
            ["title"] = title,
            ["filters"] = new { q = "" },
        });
}
