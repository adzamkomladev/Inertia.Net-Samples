using System.Net;
using Inertia.Net.Testing;

namespace KitchenSink.Tests;

public sealed class QueryDisabledAppFactory : AppFactory
{
    protected override bool QueryMethod => false;
}

[CollectionDefinition("QueryDisabled")]
public sealed class QueryDisabledCollection : ICollectionFixture<QueryDisabledAppFactory>;

[Collection("QueryDisabled")]
public sealed class QueryDisabledTests(QueryDisabledAppFactory app)
{
    [Fact]
    public async Task Query_demos_are_hidden_when_the_method_is_disabled()
    {
        var client = await app.SignInAsync();

        var links = await app.PageAsync(client, "/features/navigation/links");
        Assert.False(links.Prop("queryMethod")!.GetValue<bool>());

        using var response = await client.InertiaGetAsync("/features/navigation/query-visits", AppFactory.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
