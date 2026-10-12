using System.Text.Encodings.Web;

namespace ReactSqliteBlog.Infrastructure;

public sealed record EmailContent(string Subject, string Html, string Plain);

public sealed record DigestRow(string Title, string Url, int Views, int Likes, int Comments);

/// <summary>HTML + plain-text bodies. Every interpolated value in HTML goes through <see cref="E"/>.</summary>
public static class EmailTemplates
{
    private static string E(string? s) => HtmlEncoder.Default.Encode(s ?? "");

    private static string Button(string url, string label) => $$"""
        <p style="margin:24px 0"><a href="{{E(url)}}" style="background:#18181b;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:6px;display:inline-block;font-weight:600">{{E(label)}}</a></p>
        """;

    private static string Wrap(string appName, string heading, string body) => $$"""
        <!doctype html>
        <html><body style="margin:0;padding:0;background:#f4f4f5;font-family:-apple-system,Segoe UI,Helvetica,Arial,sans-serif;color:#18181b">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f5;padding:24px 0"><tr><td align="center">
        <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:#ffffff;border-radius:8px;padding:32px">
        <tr><td style="font-size:14px;color:#71717a;padding-bottom:8px">{{E(appName)}}</td></tr>
        <tr><td style="font-size:20px;font-weight:700;padding-bottom:16px">{{E(heading)}}</td></tr>
        <tr><td style="font-size:15px;line-height:1.6">{{body}}</td></tr>
        </table></td></tr></table></body></html>
        """;

    public static EmailContent Verification(string appName, string name, string link) => new(
        $"Verify your e-mail for {appName}",
        Wrap(appName, "Verify your e-mail", $"""
            <p>Hi {E(name)}, confirm your address to start posting, commenting and liking.</p>
            {Button(link, "Verify e-mail")}
            <p style="color:#71717a;font-size:13px">If the button does not work, copy this link: {E(link)}<br>If you did not sign up, ignore this message.</p>
            """),
        $"Hi {name},\n\nConfirm your e-mail address: {link}\n\nIf you did not sign up, ignore this message.");

    public static EmailContent NewComment(string appName, string authorName, string commenter, string postTitle, string excerpt, string link) => new(
        $"{commenter} commented on \"{postTitle}\"",
        Wrap(appName, "New comment on your post", $"""
            <p>Hi {E(authorName)}, <strong>{E(commenter)}</strong> commented on <strong>{E(postTitle)}</strong>:</p>
            <blockquote style="margin:16px 0;padding:8px 16px;border-left:3px solid #d4d4d8;color:#3f3f46">{E(excerpt)}</blockquote>
            {Button(link, "View comment")}
            """),
        $"Hi {authorName},\n\n{commenter} commented on \"{postTitle}\":\n\n> {excerpt}\n\n{link}");

    public static EmailContent NewLike(string appName, string authorName, string liker, string postTitle, string link) => new(
        $"{liker} liked \"{postTitle}\"",
        Wrap(appName, "Someone liked your post", $"""
            <p>Hi {E(authorName)}, <strong>{E(liker)}</strong> liked <strong>{E(postTitle)}</strong>.</p>
            {Button(link, "View post")}
            """),
        $"Hi {authorName},\n\n{liker} liked \"{postTitle}\".\n\n{link}");

    public static EmailContent WeeklyDigest(string appName, string name, IReadOnlyList<DigestRow> rows)
    {
        int views = rows.Sum(r => r.Views), likes = rows.Sum(r => r.Likes), comments = rows.Sum(r => r.Comments);
        var cells = string.Concat(rows.Select(r => $"""
            <tr><td style="padding:6px 0;border-bottom:1px solid #e4e4e7"><a href="{E(r.Url)}" style="color:#18181b">{E(r.Title)}</a></td>
            <td align="right" style="padding:6px 8px;border-bottom:1px solid #e4e4e7">{r.Views}</td>
            <td align="right" style="padding:6px 8px;border-bottom:1px solid #e4e4e7">{r.Likes}</td>
            <td align="right" style="padding:6px 0;border-bottom:1px solid #e4e4e7">{r.Comments}</td></tr>
            """));
        var html = Wrap(appName, "Your week in review", $"""
            <p>Hi {E(name)}, here is how your posts did over the last 7 days.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:14px">
            <tr style="color:#71717a"><td>Post</td><td align="right" style="padding:0 8px">Views</td><td align="right" style="padding:0 8px">Likes</td><td align="right">Comments</td></tr>
            {cells}
            <tr style="font-weight:700"><td style="padding-top:8px">Total</td><td align="right" style="padding:8px 8px 0">{views}</td><td align="right" style="padding:8px 8px 0">{likes}</td><td align="right" style="padding-top:8px">{comments}</td></tr>
            </table>
            """);
        var plain = $"Hi {name},\n\nYour last 7 days:\n\n"
            + string.Concat(rows.Select(r => $"- {r.Title}: {r.Views} views, {r.Likes} likes, {r.Comments} comments ({r.Url})\n"))
            + $"\nTotal: {views} views, {likes} likes, {comments} comments";
        return new($"Your {appName} weekly digest", html, plain);
    }

    public static EmailContent Recommendations(string appName, string name, IReadOnlyList<(string Title, string Excerpt, string Url)> posts, string unsubscribeUrl)
    {
        var items = string.Concat(posts.Select(post => $"""
            <article style="padding:16px 0;border-top:1px solid #e4e4e7"><h2 style="font-size:16px;margin:0 0 8px">{E(post.Title)}</h2>
            <p style="color:#52525b;margin:0 0 12px">{E(post.Excerpt)}</p><p><a href="{E(post.Url)}" style="color:#18181b">Read post</a></p></article>
            """));
        var html = Wrap(appName, "Posts you might have missed", $"""
            <p>Hi {E(name)}, here are yesterday's new posts that match your interests and that you haven't seen.</p>
            {items}<p style="font-size:12px;color:#71717a"><a href="{E(unsubscribeUrl)}">Unsubscribe from daily recommendations</a></p>
            """);
        var plain = $"Hi {name}, here are yesterday's posts you might have missed:\n\n"
            + string.Join("\n\n", posts.Select(post => $"{post.Title}\n{post.Excerpt}\n{post.Url}"))
            + $"\n\nUnsubscribe from daily recommendations: {unsubscribeUrl}";
        return new("Posts you might have missed", html, plain);
    }
}
