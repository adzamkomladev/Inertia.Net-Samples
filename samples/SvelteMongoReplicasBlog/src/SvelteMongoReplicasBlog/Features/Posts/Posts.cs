using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Features.Discovery;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.PostsHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class Posts
{
    public static void MapPosts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts", FeedAsync);
        app.MapInertia("/posts/create", "Posts/Create").RequireAuthorization("verified");
        app.MapPost("/posts", CreateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        app.MapGet("/posts/{slug}", ShowAsync);
        app.MapGet("/posts/{slug}/edit", EditAsync).RequireAuthorization();
        app.MapPut("/posts/{id:guid}", UpdateAsync)
            .RequireAuthorization("verified")
            .WithInertiaPrecognition()
            .RequireRateLimiting("writes");
        app.MapPost("/posts/{id:guid}/view", TrackViewAsync)
            .RequireRateLimiting("views");
        app.MapDelete("/posts/{id:guid}", DeleteAsync)
            .RequireAuthorization()
            .RequireRateLimiting("writes");
    }

    static async Task<IResult> FeedAsync(
        HttpContext ctx,
        AppDb db,
        Storage storage,
        OptionalServiceNamespace names,
        [FromServices] MeilisearchTaskClient? search,
        IConfiguration config,
        string? q,
        int? page,
        string? engine,
        string? cursor,
        CancellationToken ct
    )
    {
        var feed = await PostsService.FeedDataAsync(
            ctx.User.UserIdOrNull(),
            db,
            storage,
            names,
            search,
            config,
            q,
            page,
            engine,
            cursor,
            ct
        );
        if (feed.BadEngine)
            return Results.BadRequest("Invalid search engine.");
        if (feed.BadCursor)
            return Results.BadRequest("Invalid cursor.");
        return Render(
            "Posts/Index",
            new InertiaProps
            {
                ["posts"] = feed.HasSearch
                    ? Scroll(new FeedPage(feed.Data, feed.Page, feed.HasMore), "data")
                    : Scroll(new CursorPage<PostSummary>(feed.Data, cursor, feed.Next), "data"),
                ["filters"] = new { q = feed.Term ?? "" },
                ["searchEngine"] = Always(feed.ExternalSearch ? "meilisearch" : "native"),
            }
        );
    }

    static async Task<IResult> ShowAsync(
        string slug,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var uid = ctx.User.UserIdOrNull();
        var d = await PostsService.LoadDetail(db, storage, slug, uid, ct);
        if (d is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return Render(
            "Posts/Show",
            new InertiaProps
            {
                ["post"] = d,
                ["comments"] = Defer(
                    async (CancellationToken c) =>
                        (
                            await CommentsService.LoadPageAsync(
                                db,
                                storage,
                                d.Id,
                                d.Author.Id,
                                uid,
                                null,
                                c
                            )
                        ).Data,
                    group: "comments"
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
        CancellationToken ct
    )
    {
        var request = ctx.Request;
        if (
            request.Headers.ContainsKey("X-Inertia-Prefetch")
            || request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
        )
            return Results.NoContent();
        string? referrer = null;
        if (request.HasJsonContentType() && request.ContentLength is null or <= 4096)
        {
            if (
                ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is
                { IsReadOnly: false } limit
            )
                limit.MaxRequestBodySize = 4096;
            try
            {
                referrer = (await request.ReadFromJsonAsync<ViewBeacon>(ct))?.Referrer;
            }
            catch (Exception error)
                when (error is System.Text.Json.JsonException or BadHttpRequestException) { }
        }

        var uid = ctx.User.UserIdOrNull();
        var author = await PostsService.TrackViewAsync(
            id,
            uid,
            db,
            discovery,
            DateTime.UtcNow,
            ct
        );
        if (author is null)
            return Results.NotFound();
        views.Track(
            new ViewVisit(
                uid,
                ctx.Connection.RemoteIpAddress?.ToString(),
                request.Headers.UserAgent.ToString(),
                referrer is { Length: <= 2048 } ? referrer : request.Headers.Referer.ToString(),
                request.Host.Host,
                request.Headers.ContainsKey("X-Inertia-Partial-Component")
            ),
            id,
            author.Value
        );
        return Results.NoContent();
    }

    static async Task<IResult> EditAsync(
        string slug,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var d = await PostsService.LoadDetail(db, storage, slug, ctx.User.UserIdOrNull(), ct);
        if (d is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        return d.CanEdit
            ? Render("Posts/Edit", new InertiaProps { ["post"] = d })
            : HttpExtensions.Forbidden();
    }

    static async Task<IResult> CreateAsync(
        PostInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.CreateAsync(
                input,
                ctx.User.UserId(),
                db,
                jobs,
                config,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> UpdateAsync(
        Guid id,
        PostInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.UpdateAsync(
                id,
                input,
                ctx.User.UserId(),
                db,
                jobs,
                config,
                realtime,
                ct
            )
        ).Respond(ctx);

    static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IConfiguration config,
        RealtimeEvents realtime,
        CancellationToken ct
    ) =>
        (
            await PostsService.DeleteAsync(
                id,
                ctx.User.UserId(),
                db,
                jobs,
                config,
                realtime,
                ct
            )
        ).Respond(ctx);
}
