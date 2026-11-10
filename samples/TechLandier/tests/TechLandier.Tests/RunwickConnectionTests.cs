using Microsoft.Extensions.Configuration;
using Npgsql;
using TechLandier.Infrastructure.Setup;

namespace TechLandier.Tests;

public class RunwickConnectionTests
{
    private static IConfiguration Config(string? database, string? runwick = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = database,
            ["Runwick:DatabaseUrl"] = runwick,
        }).Build();

    [Fact]
    public void Pooled_neon_host_uses_the_direct_endpoint()
    {
        var connection = new NpgsqlConnectionStringBuilder(RunwickConnection.Resolve(Config(
            "postgresql://user:pass@ep-cool-name-123456-pooler.eu-central-1.aws.neon.tech/techlandier?sslmode=require")));

        Assert.Equal("ep-cool-name-123456.eu-central-1.aws.neon.tech", connection.Host);
        Assert.Equal("techlandier", connection.Database);
        Assert.Equal(SslMode.Require, connection.SslMode);
    }

    [Fact]
    public void Direct_neon_host_is_unchanged()
    {
        var connection = new NpgsqlConnectionStringBuilder(RunwickConnection.Resolve(Config(
            "postgresql://user:pass@ep-cool-name-123456.eu-central-1.aws.neon.tech/techlandier")));

        Assert.Equal("ep-cool-name-123456.eu-central-1.aws.neon.tech", connection.Host);
    }

    [Fact]
    public void Explicit_pooled_neon_url_also_uses_the_direct_endpoint()
    {
        var connection = new NpgsqlConnectionStringBuilder(RunwickConnection.Resolve(Config(
            "postgresql://user:pass@ep-direct.eu-central-1.aws.neon.tech/techlandier",
            "postgresql://runwick:secret@ep-explicit-123456-pooler.eu-central-1.aws.neon.tech/techlandier")));

        Assert.Equal("ep-explicit-123456.eu-central-1.aws.neon.tech", connection.Host);
        Assert.Equal("runwick", connection.Username);
    }
}
