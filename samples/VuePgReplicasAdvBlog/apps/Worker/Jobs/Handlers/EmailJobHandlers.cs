using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Comments;
using VuePgReplicasAdvBlog.Features.Email;
using VuePgReplicasAdvBlog.Features.Likes;
using VuePgReplicasAdvBlog.Features.Uploads;

namespace VuePgReplicasAdvBlog.Infrastructure;

public sealed class SendEmailHandler(Mailer mailer) : IJobHandler<SendEmail>
{
    public Task HandleAsync(SendEmail m, CancellationToken ct) => mailer.SendAsync(m.To, m.Subject, m.Html, m.Text, ct);
}
public sealed class DeleteAssetsHandler(Storage storage) : IJobHandler<DeleteAssets>
{
    public Task HandleAsync(DeleteAssets m, CancellationToken ct) => storage.DeleteAsync(m.Keys, ct);
}
public sealed class NotifyCommentHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<NotifyComment>
{
    public async Task HandleAsync(NotifyComment m, CancellationToken ct)
    {
        var c = await db.Comments.AsNoTracking().Include(x => x.User).Include(x => x.Post).ThenInclude(p => p.Author).FirstOrDefaultAsync(x => x.Id == m.CommentId, ct);
        if (c is null) return;
        var author = c.Post.Author;
        if (author.Id == c.UserId || author.EmailVerifiedAt is null || !author.Preferences.EmailOnComment) return;

        var excerpt = c.Body.Length > 200 ? c.Body[..200] + "ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦" : c.Body;
        await Send(Emails.NewComment(author.Email, c.User.Name, c.Post.Title, $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/posts/{c.Post.Slug}#comment-{c.Id}", excerpt), ct);
    }

    Task Send(SendEmail m, CancellationToken ct) => mailer.SendAsync(m.To, m.Subject, m.Html, m.Text, ct);
}
public sealed class NotifyLikeHandler(AppDb db, Mailer mailer, IConfiguration config) : IJobHandler<NotifyLike>
{
    public async Task HandleAsync(NotifyLike m, CancellationToken ct)
    {
        if (!await db.Likes.AnyAsync(l => l.PostId == m.PostId && l.UserId == m.LikerId, ct)) return;
        var post = await db.Posts.AsNoTracking().Include(p => p.Author).FirstOrDefaultAsync(p => p.Id == m.PostId, ct);
        var liker = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == m.LikerId, ct);
        if (post is null || liker is null) return;
        var author = post.Author;
        if (author.Id == liker.Id || author.EmailVerifiedAt is null || !author.Preferences.EmailOnLike) return;

        // ponytail: debounce = a Done NotifyLike for this post created in the last hour (LIKE on the serialized PostId; no CompletedAt column). Fine for thousands of jobs/hour; add an indexed key column if it must be exact.
        var since = DateTime.UtcNow.AddHours(-1);
        var pattern = $"%\"PostId\":{m.PostId},%";
        if (await db.Jobs.AnyAsync(j => j.Type == nameof(NotifyLike) && j.Status == JobStatus.Done && j.CreatedAt >= since && EF.Functions.Like(j.Payload, pattern), ct)) return;

        await Send(Emails.NewLike(author.Email, liker.Name, post.Title, $"{(config.AppBaseUrl() ?? "http://localhost:5199")}/posts/{post.Slug}"), ct);
    }
    Task Send(SendEmail m, CancellationToken ct) => mailer.SendAsync(m.To, m.Subject, m.Html, m.Text, ct);
}
