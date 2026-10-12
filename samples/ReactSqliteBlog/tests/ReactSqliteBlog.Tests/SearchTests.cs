using System.Net;
using Inertia.Net.Testing;
using ReactSqliteBlog.Features.Posts;

namespace ReactSqliteBlog.Tests;

public class SearchUnitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"*():^")]
    public void Empty_input_gives_null(string? q)
    {
        Assert.Null(Search.ToFtsQuery(q));
    }

    [Fact]
    public void Tokens_are_quoted_with_prefix_star()
    {
        Assert.Equal("\"hello\"* \"world\"*", Search.ToFtsQuery("hello world"));
        Assert.Equal("\"hello\"*", Search.ToFtsQuery("  hello  "));
    }

    [Theory]
    [InlineData("\"quoted\"", "\"quoted\"*")]
    [InlineData("pre*", "\"pre\"*")]
    [InlineData("(a) b:c", "\"a\"* \"bc\"*")]
    [InlineData("NEAR( a b )", "\"NEAR\"* \"a\"* \"b\"*")]
    [InlineData("a\"* OR \"b", "\"a\"* \"OR\"* \"b\"*")]
    [InlineData("col:x ^y", "\"colx\"* \"y\"*")]
    public void Fts_syntax_is_neutralised(string input, string expected) =>
        Assert.Equal(expected, Search.ToFtsQuery(input));

    [Fact]
    public void Output_only_contains_quoted_tokens()
    {
        var q = Search.ToFtsQuery("\"x\" OR NEAR(a,b) -c {d} [e] : * ^")!;
        Assert.All(q.Split(' '), t => Assert.Matches("^\"[\\p{L}\\p{N}_-]+\"\\*$", t));
    }

    [Fact]
    public void Snippet_keeps_only_mark_tags()
    {
        var s = Search.SafeSnippet("<mark>hi</mark> <script>alert(1)</script> <b>x</b>");
        Assert.DoesNotContain("<script", s);
        Assert.DoesNotContain("<b>", s);
        Assert.StartsWith("<mark>hi</mark>", s);
    }
}

public class SearchIntegrationTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    private async Task<BlogFactory.Client> AuthorAsync()
    {
        var email = BlogFactory.NewEmail("s");
        await f.CreateUserAsync(email);
        var c = f.NewClient();
        await c.LoginOkAsync(email);
        return c;
    }

    [Fact]
    public async Task Search_finds_by_title_body_and_prefix()
    {
        var c = await AuthorAsync();
        var word = "zyx" + Guid.NewGuid().ToString("N")[..8];
        var byTitle = await c.CreatePostAsync($"Title {word} here", "nothing relevant");
        var byBody = await c.CreatePostAsync("Plain", $"the body mentions {word}tail somewhere");
        await c.CreatePostAsync("Unrelated", "other text");

        using var exact = await c.GetAsync($"/posts?q={word}");
        await exact.AssertInertiaAsync(p => p.Where("q", word).Has("posts.data", 2, first => first.Etc()));
        var page = await (await c.GetAsync($"/posts?q={word}")).GetInertiaPageAsync();
        var ids = page.Prop("posts.data")!.AsArray().Select(e => Guid.Parse((string)e!["id"]!)).ToList();
        Assert.Contains(byTitle, ids);
        Assert.Contains(byBody, ids); // prefix: "{word}tail" matches "{word}*"

        using var none = await c.GetAsync("/posts?q=qqqnomatchqqq");
        await none.AssertInertiaAsync(p => p.Has("posts.data", 0));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("NEAR(")]
    [InlineData("a AND OR NOT *")]
    [InlineData("col:x -y (z")]
    [InlineData("- -")]
    [InlineData("\"*()-:^")]
    public async Task Fts_operators_do_not_cause_500(string q)
    {
        var c = await AuthorAsync();
        using var r = await c.GetAsync("/posts?q=" + Uri.EscapeDataString(q));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Snippet_has_no_raw_script_tag()
    {
        var c = await AuthorAsync();
        var word = "xss" + Guid.NewGuid().ToString("N")[..8];
        await c.CreatePostAsync("XSS", $"{word} <script>alert(1)</script> trailing");
        var page = await (await c.GetAsync($"/posts?q={word}")).GetInertiaPageAsync();
        var snippet = (string)page.Prop("posts.data.0.snippet")!;
        Assert.DoesNotContain("<script", snippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<mark>", snippet);
    }
}
