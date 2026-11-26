using Microsoft.Playwright;

namespace KitchenSink.BrowserTests;

[Collection("Browser")]
public sealed class BrowserTests(BrowserAppFixture app)
{
    [Fact]
    public async Task Login_reaches_the_dashboard_and_logout_returns_to_login()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();

        await app.SignInAsync(page);
        await page.GetByText("Recent Activity").WaitForAsync();

        await page.Locator("[data-test=\"sidebar-menu-button\"]").ClickAsync();
        await page.Locator("[data-test=\"logout-button\"]").ClickAsync();
        await page.WaitForURLAsync("**/login");
    }

    [Fact]
    public async Task Contact_can_be_created_opened_and_edited()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        var name = "Browser Person " + Guid.NewGuid().ToString("N")[..6];
        await page.GotoAsync("/contacts/create");
        await page.FillAsync("#first_name", name);
        await page.FillAsync("#last_name", "Test");
        await page.FillAsync("#email", "browser@example.com");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create Contact" }).ClickAsync();

        await page.WaitForURLAsync("**/contacts/*");
        await page.GetByText(name).First.WaitForAsync();
        await page.GetByText("Notes").First.WaitForAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await page.WaitForURLAsync("**/edit");
        Assert.Equal(name, await page.InputValueAsync("#first_name"));
        Assert.Equal("Test", await page.InputValueAsync("#last_name"));
    }

    [Fact]
    public async Task Contacts_list_uses_infinite_scroll_pages()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/contacts");
        await page.Locator("[data-infinite-scroll-page]").First.WaitForAsync();
        Assert.True(await page.Locator("[data-infinite-scroll-page]").CountAsync() > 1);
    }

    [Fact]
    public async Task Use_form_shows_validation_and_success_flash()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/features/forms/use-form");
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
        await page.GetByText("The name field is required.", new() { Exact = true }).WaitForAsync();

        await page.FillAsync("#name", "John Doe");
        await page.FillAsync("#email", "john@example.com");
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
        await page.GetByText("Form submitted successfully! Name: John Doe").WaitForAsync();
    }

    [Fact]
    public async Task Deferred_props_arrive_after_the_page()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/features/data-loading/deferred-props");
        await page.GetByText("Loaded instantly").WaitForAsync();
        await page.GetByText("Total Contacts", new() { Exact = true }).WaitForAsync();
        await page.GetByText("Report loaded").Or(page.GetByText("Failed to load report")).First.WaitForAsync();
    }

    [Fact]
    public async Task Live_props_vote_over_signalr()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/features/data-loading/live-props");
        await page.GetByRole(AriaRole.Button, new() { Name = "Vote Vue" }).ClickAsync();
        await page.GetByText("Your vote:").WaitForAsync();

        await page.Locator("[data-test=\"votes-vue\"]").WaitForAsync();
        Assert.Contains("vote", await page.Locator("[data-test=\"votes-vue\"]").InnerTextAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Send 20 Events" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector('[data-test=\"tick-events\"]')?.textContent) > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    [Fact]
    public async Task Location_event_intercepts_a_new_asset_version()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/features/events/location-event");
        await page.GetByRole(AriaRole.Button, new() { Name = "Deploy new version" }).ClickAsync();
        await page.GetByText("A new version of this app is available").WaitForAsync();
        await page.GetByText("versionChange: true").WaitForAsync();
    }

    [Fact]
    public async Task Layout_props_add_and_reset_the_subtitle_banner()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/features/layouts/layout-props");
        await page.FillAsync("#subtitle", "Hello from the layout");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await page.Locator("[data-test=\"layout-subtitle\"]").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();
        await page.Locator("[data-test=\"layout-subtitle\"]").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    [Fact]
    public async Task Big_integers_stay_exact_and_can_be_toggled()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        await app.SignInAsync(page);

        await page.GotoAsync("/features/data-loading/big-integers");
        await page.Locator("[data-test=\"order-id\"]").WaitForAsync();
        Assert.Equal("900719925474099988", (await page.Locator("[data-test=\"order-id\"]").InnerTextAsync()).Trim());
        Assert.Equal("bigint", (await page.Locator("[data-test=\"order-id-type\"]").InnerTextAsync()).Trim());

        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Order ID" }).ClickAsync();
        await page.Locator("[data-test=\"received-order-id\"]").WaitForAsync();
        Assert.Contains("900719925474099988", await page.Locator("[data-test=\"received-order-id\"]").InnerTextAsync());

        await page.GetByRole(AriaRole.Link, new() { Name = "Disabled" }).ClickAsync();
        await page.GetByText("Rounded", new() { Exact = true }).WaitForAsync();
        Assert.Equal("900719925474100000", (await page.Locator("[data-test=\"order-id\"]").InnerTextAsync()).Trim());

        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Order ID" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-test=\"received-order-id\"]')?.textContent.includes('900719925474100000')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Unknown_urls_render_the_error_page()
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();

        await page.GotoAsync("/this-page-does-not-exist");
        await page.GetByText("Not Found").WaitForAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Go Home" }).WaitForAsync();
    }

    public static TheoryData<string, string> Pages => new()
    {
        { "/contacts", "Contacts" },
        { "/contacts/create", "Create Contact" },
        { "/organizations", "Organizations" },
        { "/dashboard", "Recent Activity" },
        { "/features/forms/use-form", "useForm" },
        { "/features/forms/form-component", "Form Component" },
        { "/features/forms/file-uploads", "File Uploads" },
        { "/features/forms/validation", "Validation" },
        { "/features/forms/precognition", "Precognition" },
        { "/features/forms/optimistic-updates", "Optimistic Updates" },
        { "/features/forms/use-form-context", "useFormContext" },
        { "/features/forms/dotted-keys", "Dotted Keys" },
        { "/features/forms/wayfinder", "Wayfinder" },
        { "/features/navigation/links", "Links & Methods" },
        { "/features/navigation/preserve-state", "Preserve State" },
        { "/features/navigation/preserve-scroll", "Preserve Scroll" },
        { "/features/navigation/view-transitions", "View Transitions" },
        { "/features/navigation/history-management", "History Management" },
        { "/features/navigation/async-requests", "Async Requests" },
        { "/features/navigation/manual-visits", "Client-Side Visits" },
        { "/features/navigation/instant-visits", "Instant Visits" },
        { "/features/navigation/redirects", "Redirects" },
        { "/features/navigation/scroll-management", "Scroll Management" },
        { "/features/navigation/url-fragments", "URL Fragments" },
        { "/features/data-loading/deferred-props", "Deferred Props" },
        { "/features/data-loading/partial-reloads", "Partial Reloads" },
        { "/features/data-loading/infinite-scroll", "Infinite Scroll" },
        { "/features/data-loading/when-visible", "When Visible" },
        { "/features/data-loading/polling", "Polling" },
        { "/features/data-loading/prop-merging", "Prop Merging" },
        { "/features/data-loading/once-props/1", "Once Props" },
        { "/features/data-loading/optional-props", "Optional Props" },
        { "/features/data-loading/big-integers", "Big Integers" },
        { "/features/data-loading/live-props", "Live Props" },
        { "/features/prefetching/link-prefetch", "Link Prefetch" },
        { "/features/prefetching/stale-while-revalidate", "Stale While Revalidate" },
        { "/features/prefetching/manual-prefetch", "Manual Prefetch" },
        { "/features/prefetching/cache-management", "Cache Management" },
        { "/features/state/remember", "Remember" },
        { "/features/state/flash-data", "Flash Data" },
        { "/features/state/shared-props", "Shared Props" },
        { "/features/layouts/persistent-layouts", "Persistent Layouts" },
        { "/features/layouts/persistent-layouts/page-2", "Page 2 State" },
        { "/features/layouts/nested-layouts", "Nested Layouts" },
        { "/features/layouts/head", "Head Component" },
        { "/features/layouts/layout-props", "Layout Props" },
        { "/features/events/global-events", "Global Events" },
        { "/features/events/once-events", "Inertia.once" },
        { "/features/events/visit-callbacks", "Visit Callbacks" },
        { "/features/events/progress", "Progress Bar" },
        { "/features/events/location-event", "Location Event" },
        { "/features/errors/http-exceptions", "HTTP Exceptions" },
        { "/features/errors/network-errors", "Network Errors" },
        { "/features/http/use-http", "useHttp" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_page_renders_without_uncaught_errors(string url, string text)
    {
        if (!app.Enabled) return;
        await using var page = await app.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await app.SignInAsync(page);
        await page.GotoAsync(url);
        await page.GetByText(text, new() { Exact = true }).First.WaitForAsync();
        Assert.Empty(errors);
    }
}
