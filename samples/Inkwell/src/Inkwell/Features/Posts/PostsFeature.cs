using System.Security.Claims;
using Inkwell.Features.Analytics;
using Inkwell.Features.Social;
using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Inkwell.Features.Posts;

public static class PostsFeature
{
    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts", Feed);
        app.MapGet("/posts/{id:guid}", Show);
        app.MapGet("/posts/{id:guid}/comments", CommentPage);
        app.MapPost("/posts/{id:guid}/view", TrackView).RequireRateLimiting(RateLimits.Writes);

        var verified = app.MapGroup("").RequireAuthorization(AuthPolicies.Verified);
        verified.MapInertia("/posts/create", "Posts/Create");
        verified.MapPost("/posts", CreatePost).WithInertiaValidation();
        verified.MapGet("/posts/{id:guid}/edit", Edit);
        verified.MapPut("/posts/{id:guid}", UpdatePost).WithInertiaValidation();
        verified.MapDelete("/posts/{id:guid}", DeletePost);
        verified.MapPost("/posts/{id:guid}/comments", AddComment).WithInertiaValidation().RequireRateLimiting(RateLimits.Writes);
        verified.MapDelete("/comments/{id:guid}", DeleteComment);
        return app;
    }

    // Topic popularity is an aggregate, so a ten-minute lag after new posts is acceptable and needs no invalidation.
    static readonly TimeSpan TopicsFreshFor = TimeSpan.FromMinutes(10);
    static readonly TimeSpan TopicsStaleFor = TimeSpan.FromMinutes(5);

    static async Task<IResult> Feed(string? q, string? topic, int? page, string? cursor, PostsService posts, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Paging.TryDecode(cursor, out var after)) return Results.BadRequest("Invalid cursor.");
        var tag = PostsValidators.NormalizeTopics([topic ?? ""]) is [{ Length: > 0 } t] && PostsValidators.TopicsValid([t]) ? t : null;
        var topics = Prop(async c => await posts.PopularTopicsAsync(12, c)).Cache("posts:popular-topics", TopicsFreshFor, TopicsStaleFor);
        var term = q?.Trim() ?? "";
        if (term.Length > 0)
        {
            var found = await posts.SearchAsync(user.UserId(), term, page ?? 1, ct);
            return Render("Posts/Index", new InertiaProps
            {
                ["posts"] = Scroll(new FeedPage(found.Items, Math.Max(page ?? 1, 1), found.HasMore)),
                ["q"] = Always(term),
                ["topic"] = Always((string?)null),
                ["topics"] = topics,
            });
        }

        var recent = await posts.RecentAsync(user.UserId(), after, tag, ct);
        return Render("Posts/Index", new InertiaProps
        {
            ["posts"] = Scroll(new CursorPage<PostCardDto>(recent.Items, cursor, recent.Next)),
            ["q"] = Always(""),
            ["topic"] = Always(tag),
            ["topics"] = topics,
        });
    }

    static async Task<IResult> Show(Guid id, PostsService posts, SocialService social, ClaimsPrincipal user, CancellationToken ct)
    {
        var me = user.UserId();
        var view = await posts.GetAsync(id, me, ct);
        if (view is null) return ErrorPage.NotFound();
        return Render("Posts/Show", new InertiaProps
        {
            ["post"] = view.Post,
            ["liked"] = view.Liked,
            ["canEdit"] = me == view.Post.Author.Id,
            // The post body paints first; comments arrive in a follow-up request.
            ["comments"] = Defer(async c => await posts.CommentsAsync(id, me, view.Post.Author.Id, null, 50, c), "comments"),
            // The author card at the end of the article (bio, follow state) is below the fold.
            ["author"] = Defer(async c => await social.ProfileAsync(me, view.Post.Author.Id, c), "author"),
        });
    }

    static async Task<IResult> TrackView(Guid id, HttpContext ctx, PostsService posts, ViewTracker views, CancellationToken ct)
    {
        var headers = ctx.Request.Headers;
        if (headers.ContainsKey("X-Inertia-Prefetch")
            || headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase))
            return Results.NoContent();

        var me = ctx.User.UserId();
        if (await posts.MarkViewedAsync(id, me, ct) is not { } author) return Results.NotFound();
        await views.TrackAsync(new ViewVisit(me, ctx.Connection.RemoteIpAddress?.ToString(), headers.UserAgent.ToString()), id, author, ct);
        return Results.NoContent();
    }

    static async Task<IResult> CommentPage(Guid id, string? cursor, PostsService posts, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!Paging.TryDecode(cursor, out var after)) return Results.BadRequest("Invalid cursor.");
        var page = await posts.CommentsPageAsync(id, user.UserId(), after, ct);
        return page is null ? Results.NotFound() : Results.Json(new { data = page.Data, nextCursor = page.NextCursor });
    }

    static async Task<IResult> Edit(Guid id, PostsService posts, ClaimsPrincipal user, CancellationToken ct)
    {
        var edit = await posts.GetForEditAsync(id, user.UserId(), ct);
        if (edit is null) return ErrorPage.NotFound();
        if (edit.Forbidden) return ErrorPage.Forbidden();
        return Render("Posts/Edit", new InertiaProps { ["post"] = edit.Post });
    }

    static async Task<IResult> CreatePost([FromBody] PostInput input, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.CreateAsync(input, ctx.User.UserId()!.Value, ct)).Respond(ctx);

    static async Task<IResult> UpdatePost(Guid id, [FromBody] PostInput input, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.UpdateAsync(id, input, ctx.User.UserId()!.Value, ct)).Respond(ctx);

    static async Task<IResult> DeletePost(Guid id, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.DeleteAsync(id, ctx.User.UserId()!.Value, ct)).Respond(ctx);

    static async Task<IResult> AddComment(Guid id, [FromBody] CommentInput input, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.AddCommentAsync(id, input, ctx.User.UserId()!.Value, ct)).Respond(ctx);

    static async Task<IResult> DeleteComment(Guid id, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.DeleteCommentAsync(id, ctx.User.UserId()!.Value, ct)).Respond(ctx);
}
