using System.Text.Encodings.Web;

namespace Inkwell.Infrastructure;

/// <summary>A rendered message. <paramref name="UnsubscribeUrl"/> is set on optional mail and becomes the one-click List-Unsubscribe header.</summary>
public sealed record EmailContent(string Subject, string Html, string Plain, string? UnsubscribeUrl = null);

public sealed record DigestRow(string Title, string Url, int Views, int Likes, int Comments);

/// <summary>Where a message points its footer: the settings page and, for optional mail, a signed unsubscribe link.</summary>
public sealed record EmailFooter(string SettingsUrl, string? UnsubscribeUrl = null, string? Reason = null);

/// <summary>
/// HTML + plain-text bodies in Inkwell's paper-and-ink look: warm paper, a serif wordmark and headings, one terracotta
/// accent. Table layout and inline styles for old clients, a style block for dark mode and phones where supported,
/// a hidden preheader, and a plain-text twin for every message. Every interpolated value in HTML goes through <see cref="E"/>.
/// </summary>
public static class EmailTemplates
{
    const string Paper = "#f6f2ea", Card = "#fffdf9", Ink = "#2a241f", Muted = "#6e655c", Line = "#e8e1d5", Brand = "#a64a22", BrandSoft = "#f7ebe3";
    const string Serif = "Newsreader,Georgia,'Times New Roman',serif";
    const string Sans = "-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif";

    static string E(string? s) => HtmlEncoder.Default.Encode(s ?? "");

    static string Button(string url, string label) => $$"""
        <table role="presentation" cellpadding="0" cellspacing="0" style="margin:28px 0 8px"><tr>
        <td class="btn" style="border-radius:999px;background:{{Brand}}"><a href="{{E(url)}}" style="display:inline-block;padding:13px 26px;border-radius:999px;font:600 15px/1 {{Sans}};color:#ffffff;text-decoration:none">{{E(label)}}</a></td>
        </tr></table>
        """;

    static string Heading(string text) =>
        $"""<h1 class="ink h1" style="margin:0 0 16px;font:600 27px/1.2 {Serif};letter-spacing:-0.01em;color:{Ink}">{text}</h1>""";

    static string P(string html, string extra = "") =>
        $"""<p class="ink" style="margin:0 0 14px;font:16px/1.6 {Sans};color:{Ink}{extra}">{html}</p>""";

    static string Small(string html) =>
        $"""<p class="muted" style="margin:16px 0 0;font:13px/1.55 {Sans};color:{Muted}">{html}</p>""";

    static string Layout(string appName, string preheader, string body, EmailFooter footer)
    {
        var reason = footer.Reason is null ? "" : E(footer.Reason) + "<br>";
        var unsubscribe = footer.UnsubscribeUrl is null ? "" : $""" · <a href="{E(footer.UnsubscribeUrl)}" class="muted" style="color:{Muted};text-decoration:underline">Unsubscribe</a>""";
        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light dark"><meta name="supported-color-schemes" content="light dark">
            <title>{{E(appName)}}</title>
            <style>
              @media (max-width:620px){ .card{padding:28px 22px !important;border-radius:0 !important} .outer{padding:20px 0 !important} .edge{padding-left:22px !important;padding-right:22px !important} .h1{font-size:24px !important} }
              @media (prefers-color-scheme:dark){
                .bg{background:#1b1714 !important} .card{background:#241f1b !important;border-color:#3a332c !important}
                .ink{color:#efe9df !important} .muted{color:#b0a698 !important} .line{border-color:#3a332c !important}
                .brand{color:#e59a6e !important} .btn{background:#e59a6e !important} .btn a{color:#1b1714 !important}
                .soft{background:#2e2620 !important}
              }
            </style></head>
            <body class="bg" style="margin:0;padding:0;background:{{Paper}};-webkit-text-size-adjust:100%">
            <div style="display:none;max-height:0;overflow:hidden;mso-hide:all">{{E(preheader)}}&#8199;&#65279;&#847;&#8199;&#65279;&#847;&#8199;&#65279;&#847;&#8199;&#65279;&#847;</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" class="bg" style="background:{{Paper}}"><tr><td align="center" class="outer" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:580px">
            <tr><td class="edge" style="padding:0 6px 18px"><span class="ink" style="font:600 24px/1 {{Serif}};letter-spacing:-0.02em;color:{{Ink}}">{{E(appName)}}</span></td></tr>
            <tr><td class="card" style="background:{{Card}};border:1px solid {{Line}};border-radius:18px;padding:36px 40px">{{body}}</td></tr>
            <tr><td class="muted edge" style="padding:20px 6px 0;font:12px/1.6 {{Sans}};color:{{Muted}}">
            {{reason}}<a href="{{E(footer.SettingsUrl)}}" class="muted" style="color:{{Muted}};text-decoration:underline">Email settings</a>{{unsubscribe}}
            </td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    static string PlainFooter(EmailFooter footer) =>
        "\n\n—\n" + (footer.Reason is null ? "" : footer.Reason + "\n") + $"Email settings: {footer.SettingsUrl}"
        + (footer.UnsubscribeUrl is null ? "" : $"\nUnsubscribe: {footer.UnsubscribeUrl}");

    static string Names(IReadOnlyList<string> names, int total) => total switch
    {
        <= 1 => names.FirstOrDefault() ?? "Someone",
        2 when names.Count >= 2 => $"{names[0]} and {names[1]}",
        _ => $"{names.FirstOrDefault() ?? "Someone"} and {total - 1} others",
    };

    public static EmailContent Verification(string appName, string name, string link, EmailFooter footer)
    {
        var html = Layout(appName, $"One tap to finish setting up your {appName} account.", $"""
            {Heading("Confirm your email")}
            {P($"Hi {E(name)}, welcome to {E(appName)}. Confirm this address and you can publish, comment and like.")}
            {Button(link, "Confirm email")}
            {Small($"""Button not working? Paste this link into your browser:<br><a href="{E(link)}" class="brand" style="color:{Brand};word-break:break-all">{E(link)}</a><br><br>Didn't sign up? You can ignore this email; nothing happens until the link is used.""")}
            """, footer);
        var plain = $"Hi {name},\n\nWelcome to {appName}. Confirm your email to publish, comment and like:\n{link}\n\nDidn't sign up? Ignore this email; nothing happens until the link is used." + PlainFooter(footer);
        return new($"Confirm your email for {appName}", html, plain);
    }

    public static EmailContent NewComment(string appName, string authorName, string commenter, string postTitle, string excerpt, string link, EmailFooter footer)
    {
        var html = Layout(appName, $"{commenter}: “{excerpt}”", $"""
            {Heading($"{E(commenter)} responded to <em>{E(postTitle)}</em>")}
            {P($"Hi {E(authorName)}, there's a new response to your essay.")}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:6px 0 0"><tr>
            <td class="soft" style="background:{BrandSoft};border-left:3px solid {Brand};border-radius:4px 12px 12px 4px;padding:16px 20px">
            <p class="ink" style="margin:0;font:italic 17px/1.55 {Serif};color:{Ink}">{E(excerpt)}</p>
            <p class="muted" style="margin:8px 0 0;font:13px/1.4 {Sans};color:{Muted}">— {E(commenter)}</p>
            </td></tr></table>
            {Button(link, "Read and reply")}
            """, footer);
        var plain = $"Hi {authorName},\n\n{commenter} responded to \"{postTitle}\":\n\n> {excerpt}\n\nRead and reply: {link}" + PlainFooter(footer);
        return new($"{commenter} responded to “{postTitle}”", html, plain, footer.UnsubscribeUrl);
    }

    /// <summary>One message per batch of likes on one essay.</summary>
    public static EmailContent NewLikes(string appName, string authorName, IReadOnlyList<string> likers, int total, string postTitle, string link, EmailFooter footer)
    {
        var who = Names(likers, total);
        var html = Layout(appName, total == 1 ? $"{who} enjoyed your essay." : $"{total} readers enjoyed your essay.", $"""
            {Heading($"{E(who)} liked <em>{E(postTitle)}</em>")}
            {P(total == 1 ? $"Hi {E(authorName)}, a reader just appreciated your essay." : $"Hi {E(authorName)}, {total} readers appreciated your essay in the last quarter of an hour.")}
            {Button(link, "Open your essay")}
            """, footer);
        var plain = $"Hi {authorName},\n\n{who} liked \"{postTitle}\".\n\n{link}" + PlainFooter(footer);
        return new($"{who} liked “{postTitle}”", html, plain, footer.UnsubscribeUrl);
    }

    public static EmailContent WeeklyDigest(string appName, string name, IReadOnlyList<DigestRow> rows, EmailFooter footer)
    {
        int views = rows.Sum(r => r.Views), likes = rows.Sum(r => r.Likes), comments = rows.Sum(r => r.Comments);
        static string Stat(int value, string label) => $"""
            <td width="33%" class="line" style="padding:14px 4px;border-top:1px solid {Line};border-bottom:1px solid {Line}">
            <div class="ink" style="font:600 28px/1 {Serif};color:{Ink}">{value:N0}</div>
            <div class="muted" style="margin-top:6px;font:12px/1 {Sans};color:{Muted};text-transform:uppercase;letter-spacing:0.06em">{label}</div></td>
            """;
        static string Count(int n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";
        var list = string.Concat(rows.Take(10).Select(r => $"""
            <tr><td class="line" style="padding:14px 0;border-bottom:1px solid {Line}">
            <a href="{E(r.Url)}" class="ink" style="font:600 17px/1.35 {Serif};color:{Ink};text-decoration:none">{E(r.Title)}</a>
            <div class="muted" style="margin-top:4px;font:13px/1.4 {Sans};color:{Muted}">{Count(r.Views, "view", "views")} · {Count(r.Likes, "like", "likes")} · {Count(r.Comments, "comment", "comments")}</div>
            </td></tr>
            """));
        var html = Layout(appName, $"{views:N0} views, {likes:N0} likes and {comments:N0} comments this week.", $"""
            {Heading("Your week in review")}
            {P($"Hi {E(name)}, here's how your writing did over the last seven days.")}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:10px 0 18px;text-align:center"><tr>
            {Stat(views, "Views")}{Stat(likes, "Likes")}{Stat(comments, "Comments")}
            </tr></table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">{list}</table>
            """, footer);
        var plain = $"Hi {name},\n\nYour last 7 days: {views} views, {likes} likes, {comments} comments.\n\n"
            + string.Concat(rows.Take(10).Select(r => $"- {r.Title}: {r.Views} views, {r.Likes} likes, {r.Comments} comments\n  {r.Url}\n"))
            + PlainFooter(footer);
        return new($"Your week on {appName}: {views:N0} views", html, plain, footer.UnsubscribeUrl);
    }

    public static EmailContent Recommendations(string appName, string name, IReadOnlyList<(string Title, string Excerpt, string Url)> posts, EmailFooter footer)
    {
        var items = string.Concat(posts.Select(post => $"""
            <tr><td class="line" style="padding:18px 0;border-top:1px solid {Line}">
            <a href="{E(post.Url)}" class="ink" style="font:600 19px/1.3 {Serif};color:{Ink};text-decoration:none">{E(post.Title)}</a>
            <p class="muted" style="margin:6px 0 8px;font:15px/1.55 {Sans};color:{Muted}">{E(post.Excerpt)}</p>
            <a href="{E(post.Url)}" class="brand" style="font:600 14px/1 {Sans};color:{Brand};text-decoration:none">Read the essay →</a>
            </td></tr>
            """));
        var lead = posts.FirstOrDefault().Title;
        var html = Layout(appName, lead is null ? "New essays picked for you." : $"Starting with “{lead}”.", $"""
            {Heading("Essays you might have missed")}
            {P($"Hi {E(name)}, these came out yesterday, match what you like to read, and you haven't opened them yet.")}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:8px">{items}</table>
            """, footer);
        var plain = $"Hi {name}, essays from yesterday you might have missed:\n\n"
            + string.Join("\n\n", posts.Select(post => $"{post.Title}\n{post.Excerpt}\n{post.Url}"))
            + PlainFooter(footer);
        return new("Essays you might have missed", html, plain, footer.UnsubscribeUrl);
    }
}
