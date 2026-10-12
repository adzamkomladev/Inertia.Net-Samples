using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Infrastructure;

public sealed class SendEmailHandler(Mailer mailer) : IJobHandler<SendEmail>
{
    public Task HandleAsync(SendEmail m, CancellationToken ct) => mailer.SendAsync(m.To, m.Subject, m.Html, m.Text, ct);
}
public sealed class DeleteAssetsHandler(Storage storage) : IJobHandler<DeleteAssets>
{
    public Task HandleAsync(DeleteAssets m, CancellationToken ct) => storage.DeleteAsync(m.Keys, ct);
}
public sealed class NotifyCommentHandler(AppDb db, Mailer mailer) : IJobHandler<NotifyComment>
{
    public async Task HandleAsync(NotifyComment m, CancellationToken ct)
    {
        var c = await db.Comments.Find(x => x.Id == m.CommentId).FirstOrDefaultAsync(ct); if (c is null) return;
        var author = await db.Posts.Find(x => x.Id == c.PostId).FirstOrDefaultAsync(ct);
        var post = author;
        var user = await db.Users.Find(x => x.Id == c.UserId).FirstOrDefaultAsync(ct);
        if (post is null || user is null) return;
        var owner = await db.Users.Find(x => x.Id == post.AuthorId).FirstOrDefaultAsync(ct);
        if (owner is null || owner.Id == user.Id || owner.EmailVerifiedAt is null || !owner.Preferences.EmailOnComment) return;
        var text = c.Body.Length > 200 ? c.Body[..200] + "â€¦" : c.Body;
        await mailer.SendAsync(owner.Email, "New comment on your post", $"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>", text, ct);
    }

}
public sealed class NotifyLikeHandler(AppDb db, Mailer mailer) : IJobHandler<NotifyLike>
{
    public async Task HandleAsync(NotifyLike m, CancellationToken ct)
    {
        var post = await db.Posts.Find(x => x.Id == m.PostId).FirstOrDefaultAsync(ct);
        var liker = await db.Users.Find(x => x.Id == m.LikerId).FirstOrDefaultAsync(ct);
        if (post is null || liker is null) return;
        var owner = await db.Users.Find(x => x.Id == post.AuthorId).FirstOrDefaultAsync(ct);
        if (owner is null || owner.Id == liker.Id || owner.EmailVerifiedAt is null || !owner.Preferences.EmailOnLike) return;
        await mailer.SendAsync(owner.Email, "Someone liked your post", $"<p>{System.Net.WebUtility.HtmlEncode(liker.Name)} liked {System.Net.WebUtility.HtmlEncode(post.Title)}</p>", $"{liker.Name} liked {post.Title}", ct);
    }}
