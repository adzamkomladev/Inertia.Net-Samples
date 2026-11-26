using System.Net;
using Inertia.Net.Testing;
using KitchenSink.Data;
using KitchenSink.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KitchenSink.Tests;

[Collection("App")]
public sealed class DemoResetTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Reset_replaces_demo_data_and_keeps_the_shared_user()
    {
        var userId = await App.DbAsync(db => db.Users.Where(u => u.Email == DemoSeeder.DemoEmail).Select(u => u.Id).SingleAsync());
        var oldContactId = (await App.SeedContactAsync()).Id;
        await App.DbAsync(async db =>
        {
            db.LiveVotes.Add(new LiveVote { Visitor = "Visitor A", Framework = "vue" });
            await db.SaveChangesAsync();
        });

        var resetAt = await App.DbAsync(db => DemoSeeder.ResetAsync(db));

        await App.DbAsync(async db =>
        {
            Assert.Equal([userId], await db.Users.Select(u => u.Id).ToListAsync());
            Assert.Null(await db.Contacts.FirstOrDefaultAsync(c => c.Id == oldContactId));
            Assert.Equal(100, await db.Contacts.CountAsync());
            Assert.Equal(15, await db.Organizations.CountAsync());
            Assert.Equal(0, await db.LiveVotes.CountAsync());
            Assert.Equal(0, await db.Notes.CountAsync(n => n.UserId != userId));
        });

        Assert.NotNull(resetAt);
    }

    [Fact]
    public async Task Reset_time_is_shared_as_a_live_prop()
    {
        var page = await PageAsync("/dashboard");
        Assert.True(page.HasProp("demoResetAt"));
        new AssertableInertia(page).HasLive("demoResetAt", "public:demo", "DemoDataReset");
    }
}

[Collection("App")]
public sealed class LinksTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task The_links_page_answers_a_QUERY_visit()
    {
        using var response = await Client.InertiaRequestAsync(
            new HttpMethod("QUERY"),
            "/features/navigation/links",
            new StringContent("""{"search":"Inertia"}""", System.Text.Encoding.UTF8, "application/json"),
            AppFactory.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.GetInertiaPageAsync();
        Assert.Equal("Features/Navigation/Links", page.Component);
    }
}

[Collection("App")]
public sealed class LocationEventTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task A_version_mismatch_returns_a_location_visit()
    {
        using var response = await Client.InertiaRequestAsync(
            HttpMethod.Get,
            "/features/events/location-event/deploy",
            version: "stale-version");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "http://localhost/features/events/location-event/deploy",
            response.Headers.GetValues("X-Inertia-Location").Single());
        Assert.Equal("new-asset-version-hash", response.Headers.GetValues("X-Inertia-Version").Single());
    }
}

[Collection("App")]
public sealed class PrecognitionTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Invalid_precognition_data_returns_422_and_the_header()
    {
        using var response = await App.PostAsync(
            Client,
            "/features/forms/precognition",
            new { username = "ab" },
            configure: request =>
            {
                request.Headers.TryAddWithoutValidation("Precognition", "true");
                request.Headers.TryAddWithoutValidation("Precognition-Validate-Only", "username");
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("Precognition").Single());
    }

    [Fact]
    public async Task Valid_precognition_data_returns_success()
    {
        using var response = await App.PostAsync(
            Client,
            "/features/forms/precognition",
            new { username = "validuser" },
            configure: request =>
            {
                request.Headers.TryAddWithoutValidation("Precognition", "true");
                request.Headers.TryAddWithoutValidation("Precognition-Validate-Only", "username");
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("true", response.Headers.GetValues("Precognition").Single());
        Assert.Equal("true", response.Headers.GetValues("Precognition-Success").Single());
    }
}

[Collection("App")]
public sealed class QueryVisitTests(AppFactory app) : AppTest(app)
{
    Organization _acme = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _acme = await App.SeedOrganizationAsync("Acme");
        var globex = await App.SeedOrganizationAsync("Globex");
        await App.SeedContactAsync("John", "Doe", organizationId: _acme.Id, favorite: true);
        await App.SeedContactAsync("Jane", "Roe", organizationId: _acme.Id);
        await App.SeedContactAsync("Jack", "Smith", organizationId: globex.Id);
    }

    [Fact]
    public async Task A_plain_get_renders_every_contact()
    {
        var page = await PageAsync("/features/navigation/query-visits");

        Assert.Equal("Features/Navigation/QueryVisits", page.Component);
        Assert.Equal("GET", page.Prop("lastRequest.method")!.GetValue<string>());
        Assert.Null(page.Prop("lastRequest.body"));
        Assert.Equal(3, page.Prop("contacts")!.AsArray().Count);
    }

    [Fact]
    public async Task The_query_body_filters_contacts()
    {
        using var response = await Client.InertiaRequestAsync(
            new HttpMethod("QUERY"),
            "/features/navigation/query-visits",
            new StringContent(
                $$"""{"organizations":[{{_acme.Id}}],"favorites":true}""",
                System.Text.Encoding.UTF8,
                "application/json"),
            AppFactory.Version);

        var page = await response.GetInertiaPageAsync();
        Assert.Equal("Features/Navigation/QueryVisits", page.Component);
        Assert.Equal("QUERY", page.Prop("lastRequest.method")!.GetValue<string>());
        Assert.Single(page.Prop("contacts")!.AsArray());
        Assert.Equal("John Doe", page.Prop("contacts.0.name")!.GetValue<string>());
    }

    [Fact]
    public async Task The_query_body_applies_to_partial_reloads()
    {
        using var response = await Client.InertiaRequestAsync(
            new HttpMethod("QUERY"),
            "/features/navigation/query-visits",
            new StringContent("""{"search":"Jane"}""", System.Text.Encoding.UTF8, "application/json"),
            AppFactory.Version,
            request =>
            {
                request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Component", "Features/Navigation/QueryVisits");
                request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Data", "contacts,matches");
            });

        var page = await response.GetInertiaPageAsync();
        Assert.Single(page.Prop("contacts")!.AsArray());
        Assert.Equal("Jane Roe", page.Prop("contacts.0.name")!.GetValue<string>());
        Assert.Equal(1, page.Prop("matches")!.GetValue<int>());
        Assert.False(page.HasProp("organizations"));
    }
}

[Collection("App")]
public sealed class WayfinderPageTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task The_wayfinder_page_renders_its_sample_contact()
    {
        await App.SeedContactAsync();

        var page = await PageAsync("/features/forms/wayfinder");
        Assert.Equal("Features/Forms/Wayfinder", page.Component);
        Assert.True(page.HasProp("sampleContact"));
    }
}

[Collection("App")]
public sealed class LivePropsTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task The_live_props_page_declares_its_listeners()
    {
        var page = await PageAsync("/features/data-loading/live-props");

        Assert.Equal("Features/DataLoading/LiveProps", page.Component);
        new AssertableInertia(page)
            .HasLive("votes", "public:demo", "VoteCast")
            .HasLive("yourVote", "public:demo", "VoteCast")
            .HasLive("reactions", "public:demo", "ReactionAdded")
            .HasLive("ticks", "public:demo", "CounterTicked")
            .HasLive("slowTicks", "public:demo", "CounterTicked");
    }

    [Fact]
    public async Task One_vote_per_visitor_moves_when_they_vote_again()
    {
        await App.ResetCrmAsync();
        Client = await App.SignInAsync();
        App.Live.Clear();

        using (var first = await PostAsync("/features/data-loading/live-props/votes", new { framework = "vue" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }
        using (var second = await PostAsync("/features/data-loading/live-props/votes", new { framework = "vue" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        }
        using (var moved = await PostAsync("/features/data-loading/live-props/votes", new { framework = "react" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        }

        var page = await PageAsync("/features/data-loading/live-props");
        Assert.Equal(1, page.Prop("votes.react")!.GetValue<int>());
        Assert.Equal(0, page.Prop("votes.vue")!.GetValue<int>());
        Assert.Equal("react", page.Prop("yourVote")!.GetValue<string>());
        Assert.Contains(App.Live.Messages, message => message.Event == "VoteCast");
    }

    [Fact]
    public async Task Every_browser_session_has_its_own_vote()
    {
        await App.ResetCrmAsync();
        var first = await App.SignInAsync();
        var second = await App.SignInAsync();
        App.Live.Clear();

        using (var vote = await App.PostAsync(first, "/features/data-loading/live-props/votes", new { framework = "vue" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, vote.StatusCode);
        }
        using (var vote = await App.PostAsync(second, "/features/data-loading/live-props/votes", new { framework = "svelte" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, vote.StatusCode);
        }

        var page = await App.PageAsync(second, "/features/data-loading/live-props");
        Assert.Equal(1, page.Prop("votes.vue")!.GetValue<int>());
        Assert.Equal(1, page.Prop("votes.svelte")!.GetValue<int>());
        Assert.Equal("svelte", page.Prop("yourVote")!.GetValue<string>());
        Assert.StartsWith("Visitor ", page.Prop("visitor")!.GetValue<string>());
    }

    [Fact]
    public async Task Reactions_are_broadcast_with_their_new_counts()
    {
        await App.ResetCrmAsync();
        Client = await App.SignInAsync();
        App.Live.Clear();

        using (var response = await PostAsync("/features/data-loading/live-props/reactions", new { reaction = "party" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var page = await PageAsync("/features/data-loading/live-props");
        Assert.Equal(1, page.Prop("reactions.party")!.GetValue<int>());

        var payload = App.Live.Payloads("ReactionAdded");
        Assert.Equal(1, payload.GetProperty("__inertia").GetProperty("props").GetProperty("reactions").GetProperty("party").GetInt32());
    }

    [Fact]
    public async Task Unknown_options_are_rejected()
    {
        using (var vote = await PostAsync("/features/data-loading/live-props/votes", new { framework = "angular" }))
        {
            Assert.Equal(HttpStatusCode.Redirect, vote.StatusCode);
        }

        var page = await PageAsync("/features/data-loading/live-props");
        new AssertableInertia(page).HasError("framework");
    }
}

[Collection("App")]
public sealed class BigIntegerTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Big_integers_are_preserved_by_default_and_can_be_turned_off()
    {
        var page = await PageAsync("/features/data-loading/big-integers");
        Assert.True(page.PreserveBigIntegers);
        Assert.Equal("900719925474099988", page.PropBigInteger("orderId").ToString());

        var rounded = await PageAsync("/features/data-loading/big-integers?preserve=0");
        Assert.False(rounded.Prop("preserve")!.GetValue<bool>());

        using var response = await PostAsync("/features/data-loading/big-integers", new { id = "900719925474099988" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var after = await PageAsync("/features/data-loading/big-integers");
        Assert.Equal("900719925474099988", after.Flash["receivedOrderId"]!.GetValue<string>());
    }
}

[Collection("App")]
public sealed class ErrorPageTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Unknown_urls_render_the_404_error_page()
    {
        using var response = await Client.GetAsync("/this-page-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var page = await response.GetInertiaPageAsync();
        Assert.Equal("ErrorPage", page.Component);
        Assert.Equal(404, page.Prop("status")!.GetValue<int>());
    }

    [Theory]
    [InlineData("/features/errors/http-exceptions/403", 403)]
    [InlineData("/features/errors/http-exceptions/404", 404)]
    [InlineData("/features/errors/http-exceptions/500", 500)]
    public async Task Server_handled_errors_render_the_error_page(string url, int status)
    {
        using var response = await Client.InertiaGetAsync(url, AppFactory.Version);
        Assert.Equal(status, (int)response.StatusCode);

        var page = await response.GetInertiaPageAsync();
        Assert.Equal("ErrorPage", page.Component);
        Assert.Equal(status, page.Prop("status")!.GetValue<int>());
    }
}

[Collection("App")]
public sealed class PageSmokeTests(AppFactory app) : AppTest(app)
{
    public static TheoryData<string, string> Pages => new()
    {
        { "/contacts", "Contacts/Index" },
        { "/contacts/create", "Contacts/Create" },
        { "/organizations", "Organizations/Index" },
        { "/features/forms/use-form", "Features/Forms/UseForm" },
        { "/features/forms/form-component", "Features/Forms/FormComponent" },
        { "/features/forms/file-uploads", "Features/Forms/FileUploads" },
        { "/features/forms/validation", "Features/Forms/Validation" },
        { "/features/forms/precognition", "Features/Forms/Precognition" },
        { "/features/forms/optimistic-updates", "Features/Forms/OptimisticUpdates" },
        { "/features/forms/use-form-context", "Features/Forms/UseFormContext" },
        { "/features/forms/dotted-keys", "Features/Forms/DottedKeys" },
        { "/features/navigation/links", "Features/Navigation/Links" },
        { "/features/navigation/preserve-state", "Features/Navigation/PreserveState" },
        { "/features/navigation/preserve-scroll", "Features/Navigation/PreserveScroll" },
        { "/features/navigation/view-transitions", "Features/Navigation/ViewTransitions" },
        { "/features/navigation/history-management", "Features/Navigation/HistoryManagement" },
        { "/features/navigation/async-requests", "Features/Navigation/AsyncRequests" },
        { "/features/navigation/manual-visits", "Features/Navigation/ManualVisits" },
        { "/features/navigation/instant-visits", "Features/Navigation/InstantVisits" },
        { "/features/navigation/instant-visit-target?delay=0", "Features/Navigation/InstantVisitTarget" },
        { "/features/navigation/query-visits", "Features/Navigation/QueryVisits" },
        { "/features/navigation/redirects", "Features/Navigation/Redirects" },
        { "/features/navigation/scroll-management", "Features/Navigation/ScrollManagement" },
        { "/features/navigation/url-fragments", "Features/Navigation/UrlFragments" },
        { "/features/navigation/url-fragments/preserve-target", "Features/Navigation/UrlFragments" },
        { "/features/data-loading/deferred-props", "Features/DataLoading/DeferredProps" },
        { "/features/data-loading/partial-reloads", "Features/DataLoading/PartialReloads" },
        { "/features/data-loading/infinite-scroll", "Features/DataLoading/InfiniteScroll" },
        { "/features/data-loading/when-visible", "Features/DataLoading/WhenVisible" },
        { "/features/data-loading/polling", "Features/DataLoading/Polling" },
        { "/features/data-loading/prop-merging", "Features/DataLoading/PropMerging" },
        { "/features/data-loading/once-props/1", "Features/DataLoading/OnceProps" },
        { "/features/data-loading/once-props/2", "Features/DataLoading/OnceProps" },
        { "/features/data-loading/optional-props", "Features/DataLoading/OptionalProps" },
        { "/features/data-loading/big-integers", "Features/DataLoading/BigIntegers" },
        { "/features/data-loading/live-props", "Features/DataLoading/LiveProps" },
        { "/features/prefetching/link-prefetch", "Features/Prefetching/LinkPrefetch" },
        { "/features/prefetching/stale-while-revalidate", "Features/Prefetching/StaleWhileRevalidate" },
        { "/features/prefetching/manual-prefetch", "Features/Prefetching/ManualPrefetch" },
        { "/features/prefetching/cache-management", "Features/Prefetching/CacheManagement" },
        { "/features/state/remember", "Features/State/Remember" },
        { "/features/state/flash-data", "Features/State/FlashData" },
        { "/features/state/shared-props", "Features/State/SharedProps" },
        { "/features/layouts/persistent-layouts", "Features/Layouts/PersistentLayouts" },
        { "/features/layouts/persistent-layouts/page-2", "Features/Layouts/PersistentLayoutsPageTwo" },
        { "/features/layouts/nested-layouts", "Features/Layouts/NestedLayouts" },
        { "/features/layouts/head", "Features/Layouts/Head" },
        { "/features/layouts/layout-props", "Features/Layouts/LayoutProps" },
        { "/features/events/global-events", "Features/Events/GlobalEvents" },
        { "/features/events/once-events", "Features/Events/OnceEvents" },
        { "/features/events/visit-callbacks", "Features/Events/VisitCallbacks" },
        { "/features/events/progress", "Features/Events/Progress" },
        { "/features/events/location-event", "Features/Events/LocationEvent" },
        { "/features/errors/http-exceptions", "Features/Errors/HttpExceptions" },
        { "/features/errors/network-errors", "Features/Errors/NetworkErrors" },
        { "/features/http/use-http", "Features/Http/UseHttp" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_showcase_page_renders(string url, string component)
    {
        var page = await PageAsync(url);
        Assert.Equal(component, page.Component);
    }
}
