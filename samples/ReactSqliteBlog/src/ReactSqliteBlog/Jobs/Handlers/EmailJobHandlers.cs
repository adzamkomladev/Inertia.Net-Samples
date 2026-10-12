using System.Text;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ReactSqliteBlog.Jobs.Handlers;

public sealed class SendVerificationEmailHandler(UserManager<AppUser> users, EmailSender email, IOptions<AppOptions> app)
    : IJobHandler<SendVerificationEmail>
{
    public async Task HandleAsync(SendVerificationEmail job, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(job.UserId.ToString());
        if (user is null || user.EmailConfirmed) return;

        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var link = $"{app.Value.BaseUrl.TrimEnd('/')}/email/verify/{user.Id}/{token}";
        var m = EmailTemplates.Verification(app.Value.Name, user.DisplayName, link);
        await email.SendAsync(user.Email!, user.DisplayName, m.Subject, m.Html, m.Plain, ct, "verification");
    }
}

public sealed class CommentNotificationHandler(AppDb db, EmailSender email, IOptions<AppOptions> app) : IJobHandler<CommentNotification>
{
    public async Task HandleAsync(CommentNotification job, CancellationToken ct)
    {
        var c = await db.Comments.AsNoTracking().Where(c => c.Id == job.CommentId)
            .Select(c => new { c.Body, c.AuthorId, Commenter = c.Author.DisplayName, c.PostId, c.Post.Title, Owner = c.Post.Author })
            .FirstOrDefaultAsync(ct);
        if (c is null || c.Owner.Id == c.AuthorId || !c.Owner.EmailConfirmed || !c.Owner.Preferences.EmailOnComment) return;

        var excerpt = c.Body.Length > 200 ? c.Body[..200] + "…" : c.Body;
        var m = EmailTemplates.NewComment(app.Value.Name, c.Owner.DisplayName, c.Commenter, c.Title, excerpt, $"{app.Value.BaseUrl.TrimEnd('/')}/posts/{c.PostId}");
        await email.SendAsync(c.Owner.Email!, c.Owner.DisplayName, m.Subject, m.Html, m.Plain, ct, "comment");
    }
}

public sealed class LikeNotificationHandler(AppDb db, EmailSender email, IOptions<AppOptions> app) : IJobHandler<LikeNotification>
{
    public async Task HandleAsync(LikeNotification job, CancellationToken ct)
    {
        if (!await db.PostLikes.AnyAsync(l => l.PostId == job.PostId && l.UserId == job.LikerId, ct)) return;
        var p = await db.Posts.AsNoTracking().Where(p => p.Id == job.PostId)
            .Select(p => new { p.Title, Owner = p.Author }).FirstOrDefaultAsync(ct);
        var liker = await db.Users.AsNoTracking().Where(u => u.Id == job.LikerId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        if (p is null || liker is null || p.Owner.Id == job.LikerId || !p.Owner.EmailConfirmed || !p.Owner.Preferences.EmailOnLike) return;

        var m = EmailTemplates.NewLike(app.Value.Name, p.Owner.DisplayName, liker, p.Title, $"{app.Value.BaseUrl.TrimEnd('/')}/posts/{job.PostId}");
        await email.SendAsync(p.Owner.Email!, p.Owner.DisplayName, m.Subject, m.Html, m.Plain, ct, "like");
    }
}

public sealed class WeeklyDigestEmailHandler(AppDb db, EmailSender email, IOptions<AppOptions> app, TimeProvider time) : IJobHandler<WeeklyDigestEmail>
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

        var m = EmailTemplates.WeeklyDigest(app.Value.Name, user.DisplayName, rows);
        await email.SendAsync(user.Email!, user.DisplayName, m.Subject, m.Html, m.Plain, ct, "digest");
    }
}
