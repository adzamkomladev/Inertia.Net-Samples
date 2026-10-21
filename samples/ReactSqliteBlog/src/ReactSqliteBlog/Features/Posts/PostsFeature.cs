using Microsoft.AspNetCore.Mvc;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Features.Analytics;
using ReactSqliteBlog.Features.Discovery;
using ReactSqliteBlog.Features.Social;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using System.Security.Claims;
using static ReactSqliteBlog.Features.Posts.PostsHelpers;

namespace ReactSqliteBlog.Features.Posts;

public static partial class PostsFeature
{
    public static IServiceCollection AddPostsFeature(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered before /posts/{id:guid}-style routes; "create" is not a guid so there is no clash.
        app.MapGet("/posts", Feed);
        app.MapGet("/posts/{id:guid}", Show);
        app.MapGet("/posts/{id:guid}/comments", CommentPage);
        app.MapPost("/posts/{id:guid}/view", TrackViewAsync)
            .RequireRateLimiting(RateLimits.Writes);
        var verified = app.MapGroup("").RequireAuthorization(AuthPolicies.Verified);
        verified.MapInertia("/posts/create", "Posts/Create");
        verified.MapPost("/posts", CreatePost).WithInertiaValidation();
        verified.MapGet("/posts/{id:guid}/edit", Edit);
        verified.MapPut("/posts/{id:guid}", UpdatePost).WithInertiaValidation();
        verified.MapDelete("/posts/{id:guid}", DeletePost);
        verified.MapPost("/posts/{id:guid}/like", ToggleLike).RequireRateLimiting(RateLimits.Writes);
        verified
            .MapPost("/posts/{id:guid}/comments", AddComment)
            .WithInertiaValidation()
            .RequireRateLimiting(RateLimits.Writes);
        verified.MapDelete("/comments/{id:guid}", DeleteComment);
        return app;
    }

    static async Task<IResult> Feed(
        string? q,
        int? page,
        string? engine,
        string? cursor,
        HttpContext ctx,
        AppDb db,
        OptionalServiceNamespace names,
        [FromServices] MeilisearchTaskClient? search,
        IConfiguration config,
        ILoggerFactory logs,
        CancellationToken ct
    )
    {
        var feed = await PostsService.FeedAsync(
            db,
            ctx.User.UserId(),
            q,
            page,
            engine,
            cursor,
            search,
            config["SEARCH_DRIVER"] == "meilisearch",
            names,
            logs.CreateLogger("PostFeed"),
            ct
        );
        if (feed.BadCursor)
            return Results.BadRequest("Invalid cursor.");
        if (feed.BadEngine)
            return Results.BadRequest("Invalid search engine.");
        return Render(
            "Posts/Index",
            new InertiaProps
            {
                ["posts"] = feed.HasSearch
                    ? Scroll(new FeedPage(feed.Items, feed.Page, feed.HasMore))
                    : Scroll(new CursorPage<PostCardDto>(feed.Items, cursor, feed.Next)),
                ["q"] = Always(feed.Term ?? ""),
                ["searchEngine"] = Always(feed.ExternalSearch ? "meilisearch" : "native"),
            }
        );
    }

    static async Task<IResult> Show(
        Guid id,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var view = await PostsService.PostViewAsync(db, id, ctx.User.UserId(), ct);
        if (view is null)
            return ErrorPage.NotFound();
        return Render(
            "Posts/Show",
            new InertiaProps
            {
                ["post"] = view.Post,
                ["liked"] = view.Liked,
                ["canEdit"] = view.CanEdit, // Deferred: the post body paints first, comments arrive in a follow-up request.
                ["comments"] = Defer(
                    ct => PostsService.LoadCommentsAsync(
                        db,
                        id,
                        ctx.User.UserId(),
                        view.AuthorId,
                        null,
                        50,
                        ct
                    ),
                    "comments"
                ),
            }
        );
    }

    static async Task<IResult> TrackViewAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        ViewTracker views,
        DiscoveryService discovery,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var request = ctx.Request;
        if (
            request.Headers.ContainsKey("X-Inertia-Prefetch")
            || request.Headers["Purpose"]
                .ToString()
                .Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || request.Headers["Sec-Purpose"]
                .ToString()
                .Contains("prefetch", StringComparison.OrdinalIgnoreCase)
        )
            return Results.NoContent();
        var uid = ctx.User.UserId();
        var author = await PostsService.TrackViewAsync(
            db,
            id,
            uid,
            discovery,
            time.GetUtcNow().UtcDateTime,
            ct
        );
        if (author is null)
            return Results.NotFound();
        await views.TrackAsync(
            new ViewVisit(
                uid,
                ctx.Connection.RemoteIpAddress?.ToString(),
                request.Headers.UserAgent.ToString()
            ),
            id,
            author.Value,
            ct
        );
        return Results.NoContent();
    }

    static async Task<IResult> CommentPage(
        Guid id,
        string? cursor,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var page = await PostsService.CommentsPageAsync(db, id, ctx.User.UserId(), cursor, ct);
        if (page.BadCursor)
            return Results.BadRequest("Invalid cursor.");
        if (page.NotFound)
            return Results.NotFound();
        return Results.Json(new { data = page.Data, nextCursor = page.NextCursor });
    }

    static async Task<IResult> Edit(
        Guid id,
        ClaimsPrincipal user,
        AppDb db,
        CancellationToken ct
    )
    {
        var view = await PostsService.PostForEditAsync(db, id, user.UserId(), ct);
        if (view is null)
            return ErrorPage.NotFound();
        if (view.Forbidden)
            return ErrorPage.Forbidden();
        return Render("Posts/Edit", new InertiaProps { ["post"] = view.Post });
    }

    static async Task<IResult> CreatePost(
        [FromBody] PostInput input,
        HttpContext ctx,
        ClaimsPrincipal user,
        AppDb db,
        JobQueue queue,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.CreateAsync(input, user.UserId()!.Value, db, queue, time, realtime, ct)
        ).Respond(ctx);

    static async Task<IResult> UpdatePost(
        Guid id,
        [FromBody] PostInput input,
        HttpContext ctx,
        ClaimsPrincipal user,
        AppDb db,
        JobQueue queue,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.UpdateAsync(
                id,
                input,
                user.UserId()!.Value,
                db,
                queue,
                time,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> DeletePost(
        Guid id,
        HttpContext ctx,
        ClaimsPrincipal user,
        AppDb db,
        JobQueue queue,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (await PostsService.DeleteAsync(id, user.UserId()!.Value, db, queue, realtime, ct)).Respond(ctx);

    static async Task<IResult> ToggleLike(
        Guid id,
        HttpContext ctx,
        ClaimsPrincipal user,
        AppDb db,
        JobQueue queue,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.ToggleLikeAsync(
                id,
                user.UserId()!.Value,
                db,
                queue,
                discovery,
                time,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> AddComment(
        Guid id,
        [FromBody] CommentInput input,
        HttpContext ctx,
        ClaimsPrincipal user,
        AppDb db,
        JobQueue queue,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.AddCommentAsync(
                id,
                input,
                user.UserId()!.Value,
                db,
                queue,
                discovery,
                time,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> DeleteComment(
        Guid id,
        HttpContext ctx,
        ClaimsPrincipal user,
        AppDb db,
        DiscoveryService discovery,
        TimeProvider time,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.DeleteCommentAsync(
                id,
                user.UserId()!.Value,
                db,
                discovery,
                time,
                realtime,
                ct
            )
        ).Respond(ctx);
}
