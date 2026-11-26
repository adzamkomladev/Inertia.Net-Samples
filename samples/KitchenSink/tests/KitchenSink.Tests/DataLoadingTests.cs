using Inertia.Net.Testing;

namespace KitchenSink.Tests;

[Collection("App")]
public sealed class DataLoadingTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Merge_props_append_prepend_and_reset()
    {
        var page = await PageAsync("/features/data-loading/prop-merging");

        Assert.Contains("notifications", page.MergeProps);
        Assert.Contains("activities", page.PrependProps);
        Assert.Contains("contacts.id", page.MatchPropsOn);
        Assert.Single(page.Prop("contacts")!.AsArray());

        var partial = await page.ReloadOnlyAsync(Client, "contacts");
        Assert.Equal(2, partial.Prop("contacts")!.AsArray().Count);

        using var response = await Client.InertiaGetAsync(page.Url, AppFactory.Version, request =>
        {
            request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Component", page.Component);
            request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Data", "contacts");
            request.Headers.TryAddWithoutValidation("X-Inertia-Reset", "contacts");
        });
        var reset = await response.GetInertiaPageAsync();
        Assert.Single(reset.Prop("contacts")!.AsArray());
    }

    [Fact]
    public async Task Once_props_are_skipped_when_the_client_already_holds_them()
    {
        var page = await PageAsync("/features/data-loading/once-props/1");
        Assert.True(page.OnceProps.ContainsKey("staticData"));
        Assert.True(page.OnceProps.ContainsKey("expiringData"));
        Assert.True(page.OnceProps.ContainsKey("shared-once-key"));

        using var response = await Client.InertiaGetAsync("/features/data-loading/once-props/2", AppFactory.Version, request =>
            request.Headers.TryAddWithoutValidation("X-Inertia-Except-Once-Props", "staticData,freshData,expiringData,shared-once-key"));
        var second = await response.GetInertiaPageAsync();

        Assert.False(second.HasProp("staticData"));
        Assert.False(second.HasProp("aliasedData"));
        Assert.True(second.HasProp("dynamicData"));
        Assert.True(second.OnceProps.ContainsKey("staticData"));
    }

    [Fact]
    public async Task Deferred_groups_and_rescued_props_keep_their_metadata()
    {
        var page = await PageAsync("/features/data-loading/deferred-props");
        new AssertableInertia(page)
            .HasDeferred("slowStats")
            .HasDeferred("heavy", "heavyData")
            .HasDeferred("flakyReport");

        var heavy = await page.LoadDeferredPropsAsync(Client, "heavy");
        Assert.True(heavy.HasProp("heavyData"));

        using var response = await Client.InertiaGetAsync(page.Url, AppFactory.Version, request =>
        {
            request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Component", page.Component);
            request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Data", "flakyReport");
        });
        var flaky = await response.GetInertiaPageAsync();
        Assert.Contains("flakyReport", flaky.RescuedProps);
        Assert.False(flaky.HasProp("flakyReport"));
    }

    [Fact]
    public async Task Optional_props_are_only_resolved_when_requested()
    {
        var page = await PageAsync("/features/data-loading/when-visible");
        Assert.False(page.HasProp("section1"));
        Assert.False(page.HasProp("section2"));

        var loaded = await page.ReloadOnlyAsync(Client, "section1", "section2");
        Assert.True(loaded.HasProp("section1"));
        Assert.True(loaded.HasProp("section2"));
    }
}
