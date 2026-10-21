using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Analytics;
using VuePgReplicasAdvBlog.Features.Comments;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Posts;
using VuePgReplicasAdvBlog.Features.Search;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class PostsEndpoints
{
    public static void MapPosts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/posts", FeedAsync);
        app.MapInertia("/posts/create", "Posts/Create").RequireAuthorization("verified");
        app.MapPost("/posts", CreateAsync).RequireAuthorization("verified").WithInertiaPrecognition().RequireRateLimiting("writes");
        app.MapGet("/posts/{slug}", ShowAsync);
        app.MapGet("/posts/{slug}/edit", EditAsync).RequireAuthorization();
        app.MapPut("/posts/{id:guid}", UpdateAsync).RequireAuthorization("verified").WithInertiaPrecognition().RequireRateLimiting("writes");
        app.MapPost("/posts/{id:guid}/view", TrackViewAsync).RequireRateLimiting("views").AddEndpointFilter(new FeatureErrorFilter(json: true));
        app.MapDelete("/posts/{id:guid}", DeleteAsync).RequireAuthorization().RequireRateLimiting("writes");
    }

    static async Task<IResult> FeedAsync(HttpContext ctx, AppDb db, Storage storage, string? q, int? page, string? engine, string? cursor, IConfiguration config, OptionalServiceNamespace serviceNamespace, CancellationToken ct)
    {
        var feed = await PostsService.FeedAsync(ctx.User.UserIdOrNull(), db, storage, q, page, engine, cursor,
            ctx.RequestServices.GetService<MeilisearchTaskClient>(), config["SEARCH_DRIVER"] == "meilisearch", serviceNamespace, ct);
        return Render("Posts/Index", new InertiaProps
        {
            ["posts"] = feed.IsSearch
                ? Scroll(new NumberedScrollPage<PostSummary>(feed.Data, feed.Page, feed.HasMore), "data")
                : Scroll(new CursorScrollPage<PostSummary>(feed.Data, feed.Cursor, feed.NextCursor), "data"),
            ["filters"] = new { q = q ?? "" },
            ["searchEngine"] = Always(feed.SearchEngine)
        });
    }

    static async Task<IResult> CreateAsync(PostInput input, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var slug = await PostsService.CreateAsync(input, ctx.User.UserId(), db, jobs, realtime, ct);
        ctx.Inertia().Flash("toast", new Toast("success", "Post published."));
        return Results.Redirect($"/posts/{slug}");
    }

    static async Task<IResult> ShowAsync(string slug, HttpContext ctx, AppDb db, Storage storage, CancellationToken ct)
    {
        var uid = ctx.User.UserIdOrNull();
        var post = await PostsService.ShowAsync(slug, uid, db, storage, ct);
        return Render("Posts/Show", new InertiaProps
        {
            ["post"] = post,
            ["comments"] = Defer(async (CancellationToken c) =>
                (await CommentsService.LoadPageAsync(db, storage, post.Id, post.Author.Id, uid, null, c)).Data, group: "comments")
        });
    }

    static async Task<IResult> EditAsync(string slug, HttpContext ctx, AppDb db, Storage storage, CancellationToken ct) =>
        Render("Posts/Edit", new InertiaProps { ["post"] = await PostsService.EditAsync(slug, ctx.User.UserId(), db, storage, ct) });

    static async Task<IResult> UpdateAsync(Guid id, PostInput input, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        var slug = await PostsService.UpdateAsync(id, input, ctx.User.UserId(), db, jobs, realtime, ct);
        ctx.Inertia().Flash("toast", new Toast("success", "Post updated."));
        return Results.Redirect($"/posts/{slug}");
    }

    // Parse and filter the browser beacon here; analytics receives explicit visit data.
    static async Task<IResult> TrackViewAsync(Guid id, HttpContext ctx, AppDb db, ViewTracker views, DiscoveryService discovery, CancellationToken ct)
    {
        var request = ctx.Request;
        if (request.Headers.ContainsKey("X-Inertia-Prefetch")
            || request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase))
            return Results.NoContent();
        string? referrer = null;
        if (request.HasJsonContentType() && request.ContentLength is null or <= 4096)
        {
            if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = 4096;
            try { referrer = (await request.ReadFromJsonAsync<ViewBeacon>(ct))?.Referrer; }
            catch (Exception error) when (error is System.Text.Json.JsonException or BadHttpRequestException) { }
        }
        var uid = ctx.User.UserIdOrNull();
        var author = await PostsService.TrackViewAsync(id, uid, db, discovery, ct);
        if (!request.Headers.ContainsKey("X-Inertia-Partial-Component"))
            await views.TrackAsync(new ViewVisit(uid, ctx.Connection.RemoteIpAddress?.ToString(), request.Headers.UserAgent.ToString(),
                referrer is { Length: <= 2048 } ? referrer : request.Headers.Referer.ToString(), request.Host.Host), id, author, ct);
        return Results.NoContent();
    }

    static async Task<IResult> DeleteAsync(Guid id, HttpContext ctx, AppDb db, JobQueue jobs, RealtimeEvents realtime, CancellationToken ct)
    {
        await PostsService.DeleteAsync(id, ctx.User.UserId(), db, jobs, realtime, ct);
        ctx.Inertia().Flash("toast", new Toast("success", "Post deleted."));
        return Results.Redirect("/posts");
    }

    sealed record ViewBeacon(string? Referrer);
}
