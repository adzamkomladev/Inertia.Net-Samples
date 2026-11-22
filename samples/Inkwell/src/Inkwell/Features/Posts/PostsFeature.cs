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
        app.MapGet("/posts", Feed).WithName("posts.index");
        app.MapGet("/posts/{id:guid}", Show).WithName("posts.show");
        app.MapGet("/posts/{id:guid}/comments", CommentPage).WithName("posts.comments");
        app.MapPost("/posts/{id:guid}/view", TrackView).RequireRateLimiting(RateLimits.Writes).WithName("posts.view");

        var verified = app.MapGroup("").RequireAuthorization(AuthPolicies.Verified);
        verified.MapInertia("/posts/create", "Posts/Create").WithName("posts.create");
        verified.MapPost("/posts", CreatePost).WithInertiaValidation().RequireRateLimiting(RateLimits.Writes).WithName("posts.store");
        verified.MapGet("/posts/{id:guid}/edit", Edit).WithName("posts.edit");
        verified.MapPut("/posts/{id:guid}", UpdatePost).WithInertiaValidation().RequireRateLimiting(RateLimits.Writes).WithName("posts.update");
        verified.MapDelete("/posts/{id:guid}", DeletePost).RequireRateLimiting(RateLimits.Writes).WithName("posts.destroy");
        verified.MapPost("/posts/{id:guid}/comments", AddComment).WithInertiaValidation().RequireRateLimiting(RateLimits.Writes).WithName("posts.comments.store");
        verified.MapDelete("/comments/{id:guid}", DeleteComment).RequireRateLimiting(RateLimits.Writes).WithName("comments.destroy");
        return app;
    }

    // Topic popularity is an aggregate, so a ten-minute lag after new posts is acceptable and needs no invalidation.
    static readonly TimeSpan TopicsFreshFor = TimeSpan.FromMinutes(10);
    static readonly TimeSpan TopicsStaleFor = TimeSpan.FromMinutes(5);

    static async Task<IResult> Feed(string? q, string? topic, PageRequest page, CursorRequest cursor, PostsService posts, ClaimsPrincipal user, CancellationToken ct) =>
        await RenderFeedAsync(posts, user, ct, q, topic, page, cursor);

    /// <summary>The feed render shared by GET /posts and the login POST, which renders it inline after signing in.</summary>
    internal static async Task<IResult> RenderFeedAsync(PostsService posts, ClaimsPrincipal user, CancellationToken ct,
        string? q = null, string? topic = null, PageRequest? page = null, CursorRequest? cursor = null)
    {        var tag = PostsValidators.NormalizeTopics([topic ?? ""]) is [{ Length: > 0 } t] && PostsValidators.TopicsValid([t]) ? t : null;
        var topics = Prop(async c => await posts.PopularTopicsAsync(12, c)).Cache("posts:popular-topics", TopicsFreshFor, TopicsStaleFor);
        var term = q?.Trim() ?? "";
        if (term.Length > 0)
        {
            var found = await posts.SearchAsync(user.UserId(), term, page ?? new(), ct);
            return Render("Posts/Index", new InertiaProps
            {
                ["posts"] = Scroll(found, "data"),
                ["q"] = Always(term),
                ["topic"] = Always((string?)null),
                ["topics"] = topics,
            });
        }

        var recent = await posts.RecentAsync(user.UserId(), cursor ?? new(), tag, ct);
        return Render("Posts/Index", new InertiaProps
        {
            ["posts"] = Scroll(recent, "data"),
            ["q"] = Always(""),
            ["topic"] = Always(tag),
            ["topics"] = topics,
        });
    }

    static async Task<IResult> Show(Guid id, CursorRequest comments, PostsService posts, SocialService social, ClaimsPrincipal user, CancellationToken ct)
    {
        var me = user.UserId();
        var view = await posts.GetAsync(id, me, ct);
        if (view is null) return ErrorPage.NotFound();
        return ShowPage(id, me, view, posts, social, comments);
    }

    /// <summary>The essay page render shared by GET /posts/{id} and the create/update POSTs, which render it inline.</summary>
    static IResult ShowPage(Guid id, Guid? me, PostPage view, PostsService posts, SocialService social, CursorRequest? comments = null)
    {
        comments ??= new CursorRequest();
        return Render("Posts/Show", new InertiaProps
        {
            ["post"] = view.Post,
            ["liked"] = view.Liked,
            ["canEdit"] = me == view.Post.Author.Id,
            // Both sit below the essay: the client asks for them (WhenVisible) only as a reader scrolls near, so a visit that
            // stops at the article costs no comment or profile queries.
            ["comments"] = Optional(async c => await posts.CommentsAsync(id, me, view.Post.Author.Id, comments with { PerPage = 50 }, c)),
            ["author"] = Optional(async c => await social.ProfileAsync(me, view.Post.Author.Id, c)),
        });
    }

    /// <summary>
    /// Playbook: a successful write that lands on the essay page renders it inline (with the GET address as the page url)
    /// instead of redirecting and paying a second full visit. Only Inertia visits render inline; a plain form post keeps
    /// the mapped redirect. Anything else keeps the mapped outcome response.
    /// </summary>
    static async Task<IResult> RenderShowAfter(Outcome outcome, HttpContext ctx, PostsService posts, SocialService social, Guid? me, CancellationToken ct)
    {
        if (outcome is not { Errors: null, NotFound: false, Forbidden: false }
            || outcome.Redirect is not ("/posts" or null)
            || !ctx.InertiaRequest().IsInertia)
            return outcome.Respond(ctx);
        // The service returns the created/updated essay's own url ("/posts/{id}"): render that page from this request.
        if (outcome.Redirect is { } url
            && url.StartsWith("/posts/", StringComparison.Ordinal)
            && Guid.TryParse(url["/posts/".Length..], out var id)
            && await posts.GetAsync(id, me, ct) is { } view)
        {
            if (outcome.Flash is { } flash) ctx.Inertia().Flash(flash.Key, flash.Message);
            PageUrl.Next(ctx, url);
            return ShowPage(id, me, view, posts, social);
        }
        return outcome.Respond(ctx);
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

    static async Task<IResult> CommentPage(Guid id, CursorRequest cursor, PostsService posts, ClaimsPrincipal user, CancellationToken ct)
    {
        var page = await posts.CommentsPageAsync(id, user.UserId(), cursor, ct);
        return page is null
            ? Results.NotFound()
            : Results.Json(new { data = page.Data, nextCursor = page.NextCursor, hasMore = page.HasMore });
    }

    static async Task<IResult> Edit(Guid id, PostsService posts, ClaimsPrincipal user, CancellationToken ct)
    {
        var edit = await posts.GetForEditAsync(id, user.UserId(), ct);
        if (edit is null) return ErrorPage.NotFound();
        if (edit.Forbidden) return ErrorPage.Forbidden();
        return Render("Posts/Edit", new InertiaProps { ["post"] = edit.Post });
    }

    static async Task<IResult> CreatePost([FromBody] PostInput input, PostsService posts, SocialService social, HttpContext ctx, CancellationToken ct) =>
        await RenderShowAfter(await posts.CreateAsync(input, ctx.User.UserId()!.Value, ct), ctx, posts, social, ctx.User.UserId(), ct);

    static async Task<IResult> UpdatePost(Guid id, [FromBody] PostInput input, PostsService posts, SocialService social, HttpContext ctx, CancellationToken ct) =>
        await RenderShowAfter(await posts.UpdateAsync(id, input, ctx.User.UserId()!.Value, ct), ctx, posts, social, ctx.User.UserId(), ct);

    static async Task<IResult> DeletePost(Guid id, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.DeleteAsync(id, ctx.User.UserId()!.Value, ct)).Respond(ctx);

    static async Task<IResult> AddComment(Guid id, [FromBody] CommentInput input, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.AddCommentAsync(id, input, ctx.User.UserId()!.Value, ct)).Respond(ctx);

    static async Task<IResult> DeleteComment(Guid id, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        (await posts.DeleteCommentAsync(id, ctx.User.UserId()!.Value, ct)).Respond(ctx);
}
