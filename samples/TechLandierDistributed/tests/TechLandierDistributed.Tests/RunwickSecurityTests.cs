using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TechLandier.Dashboard;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

public class RunwickSecurityTests
{
    sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "TechLandier";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    static RunwickAdminCredentials Credentials(params (string Key, string Value)[] values) =>
        RunwickManagementAuth.Credentials(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build());

    [Fact]
    public void The_sample_login_is_refused_outside_Development_but_allowed_in_it()
    {
        RunwickManagementAuth.EnsureCredentialsAllowed(Credentials(), new Env("Development"));
        Assert.Throws<InvalidOperationException>(() => RunwickManagementAuth.EnsureCredentialsAllowed(Credentials(), new Env("Production")));
        Assert.Throws<InvalidOperationException>(() => RunwickManagementAuth.EnsureCredentialsAllowed(
            Credentials(("Runwick:Management:Username", "ops")), new Env("Production")));
        RunwickManagementAuth.EnsureCredentialsAllowed(
            Credentials(("Runwick:Management:Username", "ops"), ("Runwick:Management:Password", "a-long-unique-secret")), new Env("Production"));
    }

    [Fact]
    public void Demo_endpoints_default_to_Development_only()
    {
        var empty = new ConfigurationBuilder().Build();
        Assert.True(RunwickDefaults.DemoEnabled(empty, new Env("Development")));
        Assert.False(RunwickDefaults.DemoEnabled(empty, new Env("Production")));
        var on = new ConfigurationBuilder().AddInMemoryCollection([new("Runwick:Demo:Enabled", "true")]).Build();
        Assert.True(RunwickDefaults.DemoEnabled(on, new Env("Production")));
    }
}
