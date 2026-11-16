using System.Text;
using Inkwell.Data;
using Inkwell.Features.Notifications;
using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inkwell.Jobs.Handlers;

public sealed class SendVerificationEmailHandler(UserManager<AppUser> users, EmailSender email, EmailUnsubscribe unsubscribe, IOptions<AppOptions> app)
    : global::Runwick.IJobHandler<SendVerificationEmail>
{
    public async Task HandleAsync(SendVerificationEmail job, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(job.UserId.ToString());
        if (user is null || user.EmailConfirmed) return;

        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var link = $"{app.Value.BaseUrl.TrimEnd('/')}/email/verify/{user.Id}/{token}";
        var m = EmailTemplates.Verification(app.Value.Name, user.DisplayName, link, new EmailFooter(unsubscribe.SettingsUrl));
        await email.SendAsync(user.Email!, user.DisplayName, m, ct, "verification");
    }
}

public sealed class CommentNotificationHandler(AppDb db, EmailSender email, EmailUnsubscribe unsubscribe, IOptions<AppOptions> app) : global::Runwick.IJobHandler<CommentNotification>
{
    public async Task HandleAsync(CommentNotification job, CancellationToken ct)
    {
        var c = await db.Comments.AsNoTracking().Where(c => c.Id == job.CommentId)
            .Select(c => new { c.Body, c.AuthorId, Commenter = c.Author.DisplayName, c.PostId, c.Post.Title, Owner = c.Post.Author })
            .FirstOrDefaultAsync(ct);
        if (c is null || c.Owner.Id == c.AuthorId || !c.Owner.EmailConfirmed || !c.Owner.Preferences.EmailOnComment) return;

        var excerpt = Inkwell.Features.Posts.Search.Excerpt(c.Body);
        var m = EmailTemplates.NewComment(app.Value.Name, c.Owner.DisplayName, c.Commenter, c.Title, excerpt,
            $"{app.Value.BaseUrl.TrimEnd('/')}/posts/{c.PostId}#comments", unsubscribe.Footer(c.Owner.Id, EmailKind.Comments));
        await email.SendAsync(c.Owner.Email!, c.Owner.DisplayName, m, ct, "comment");
    }
}

/// <summary>
/// Runs once per essay and like window. It mails the author about that window's likes they have not already seen in the
/// app, so a busy quarter hour becomes one email, and likes the author already looked at send nothing.
/// </summary>
public sealed class LikeNotificationHandler(AppDb db, EmailSender email, EmailUnsubscribe unsubscribe, IOptions<AppOptions> app)
    : global::Runwick.IJobHandler<LikeNotification>
{
    public async Task HandleAsync(LikeNotification job, CancellationToken ct)
    {
        var p = await db.Posts.AsNoTracking().Where(p => p.Id == job.PostId)
            .Select(p => new { p.Title, Owner = p.Author }).FirstOrDefaultAsync(ct);
        if (p is null || !p.Owner.EmailConfirmed || !p.Owner.Preferences.EmailOnLike) return;

        var unread = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == p.Owner.Id && n.PostId == job.PostId && n.Kind == NotificationKinds.Like && n.ReadAt == null && n.At >= job.WindowStart && n.At < job.WindowEnd && n.ActorId != null)
            .OrderByDescending(n => n.At).Select(n => n.ActorId!.Value).ToListAsync(ct);
        // Newest first, one entry per reader, and only readers who still like it.
        var still = await db.PostLikes.Where(l => l.PostId == job.PostId && unread.Contains(l.UserId)).Select(l => l.UserId).ToListAsync(ct);
        var actors = unread.Distinct().Where(still.Contains).ToList();
        if (actors.Count == 0) return;
        var names = await db.Users.AsNoTracking().Where(u => actors.Take(2).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var likers = actors.Take(2).Where(names.ContainsKey).Select(id => names[id]).ToList();

        var m = EmailTemplates.NewLikes(app.Value.Name, p.Owner.DisplayName, likers, actors.Count, p.Title,
            $"{app.Value.BaseUrl.TrimEnd('/')}/posts/{job.PostId}", unsubscribe.Footer(p.Owner.Id, EmailKind.Likes));
        await email.SendAsync(p.Owner.Email!, p.Owner.DisplayName, m, ct, "like");
    }
}

public sealed class WeeklyDigestEmailHandler(AppDb db, EmailSender email, EmailUnsubscribe unsubscribe, IOptions<AppOptions> app, TimeProvider time) : global::Runwick.IJobHandler<WeeklyDigestEmail>
{
    public async Task HandleAsync(WeeklyDigestEmail job, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == job.UserId, ct);
        if (user is null || !user.EmailConfirmed || !user.Preferences.WeeklyDigest) return;

        var now = time.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-7);
        var sinceDay = DateOnly.FromDateTime(since);
        var baseUrl = app.Value.BaseUrl.TrimEnd('/');
        var posts = await db.Posts.AsNoTracking().Where(p => p.AuthorId == user.Id)
            .Select(p => new
            {
                p.Id,
                p.Title,
                Views = db.PostViews.Count(v => v.PostId == p.Id && v.Day >= sinceDay),
                Likes = db.PostLikes.Count(l => l.PostId == p.Id && l.CreatedAt >= since),
                Comments = db.Comments.Count(c => c.PostId == p.Id && c.CreatedAt >= since),
            }).ToListAsync(ct);

        var rows = posts.Where(p => p.Views + p.Likes + p.Comments > 0)
            .OrderByDescending(p => p.Views + p.Likes + p.Comments)
            .Select(p => new DigestRow(p.Title, $"{baseUrl}/posts/{p.Id}", p.Views, p.Likes, p.Comments)).ToList();
        if (rows.Count == 0) return;

        var m = EmailTemplates.WeeklyDigest(app.Value.Name, user.DisplayName, rows, unsubscribe.Footer(user.Id, EmailKind.Weekly));
        await email.SendAsync(user.Email!, user.DisplayName, m, ct, "digest");
    }
}
