using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Inkwell.Infrastructure;

namespace Inkwell.Tests;

public class EnvironmentTests
{
    [Theory]
    [InlineData(true, "", "https://objects.example:9000/media/uploads/a%20b.png")]
    [InlineData(false, "", "https://media.objects.example:9000/uploads/a%20b.png")]
    [InlineData(true, "https://cdn.example/images/", "https://cdn.example/images/uploads/a%20b.png")]
    public void Media_urls_use_addressing_style_or_public_override(bool pathStyle, string publicUrl, string expected)
    {
        using var storage = new Storage(Options.Create(new StorageOptions
        {
            ServiceUrl = "https://objects.example:9000/", MediaBucket = "media",
            AccessKey = "test", SecretKey = "test", ForcePathStyle = pathStyle,
            PublicBaseUrl = publicUrl,
        }));
        Assert.Equal(expected, storage.MediaUrl("uploads/a b.png"));
    }

    [Fact]
    public void Dotenv_maps_sections_and_preserves_existing_environment()
    {
        var dir = Path.Combine(Path.GetTempPath(), "inertia-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var key = "EnvTest" + Guid.NewGuid().ToString("N") + "__Name";
        var path = Path.Combine(dir, ".env");
        try
        {
            File.WriteAllText(path, key + "=\"value with spaces # literal\"\n");
            DotNetEnv.Env.NoClobber().TraversePath().Load(Path.Combine(dir, "nested", ".env"));
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            Assert.Equal("value with spaces # literal", config[key.Replace("__", ":")]);
            Environment.SetEnvironmentVariable(key, "deployment");
            DotNetEnv.Env.NoClobber().Load(path);
            Assert.Equal("deployment", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
            Directory.Delete(dir, true);
        }
    }
    [Fact]
    public void Plain_s3_names_override_legacy_section_values()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = "http://legacy:9000",
            ["S3_ENDPOINT"] = "https://objects.example",
            ["STORAGE_FORCED_PATH_STYLE"] = "false",
            ["S3_ACCESS_KEY"] = "sample-key", ["S3_SECRET_KEY"] = "sample-secret",
            ["S3_REGION"] = "eu-west-1", ["S3_BUCKET"] = "images",
            ["S3_PUBLIC_URL"] = "https://cdn.example/images",
        }).Build();
        var services = new ServiceCollection();
        services.AddStorage(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
        Assert.Equal("https://objects.example", options.ServiceUrl);
        Assert.False(options.ForcePathStyle);
        Assert.Equal("sample-key", options.AccessKey);
        Assert.Equal("sample-secret", options.SecretKey);
        Assert.Equal("eu-west-1", options.Region);
        Assert.Equal("images", options.MediaBucket);
        Assert.Equal("https://cdn.example/images", options.PublicBaseUrl);
    }
    [Fact]
    public void Public_bucket_holds_media_so_public_urls_resolve()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3_BUCKET"] = "inkwell-uploads", ["S3_PUBLIC_BUCKET"] = "inkwell-media",
            ["S3_PUBLIC_URL"] = "https://objects.example/inkwell-media",
        }).Build();
        var services = new ServiceCollection();
        services.AddStorage(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
        Assert.Equal("inkwell-media", options.MediaBucket);
        Assert.Equal("https://objects.example/inkwell-media/a/b.png", provider.GetRequiredService<Storage>().MediaUrl("a/b.png"));
    }

    [Fact]
    public void Plain_application_names_reach_existing_options_and_job_configuration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APP_NAME"] = "Sample", ["APP_BASE_URL"] = "https://blog.example",
            ["DATA_PROTECTION_KEYS_PATH"] = "local-keys", ["JOBS_CONCURRENCY"] = "8",
            ["EMAIL_API_KEY"] = "", ["Email:ApiKey"] = "legacy-key",
            ["EMAIL_FROM_ADDRESS"] = "sender@example.com", ["EMAIL_FROM_NAME"] = "Sender",
            ["ANALYTICS_SALT"] = "sample-salt", ["SSR_EXECUTABLE"] = "node",
            ["Cron:prune"] = "30 3 * * *",
        }).Build();
        config.ApplyPlainNames();
        var services = new ServiceCollection();
        services.Configure<AppOptions>(config.GetSection("App"));
        services.Configure<EmailOptions>(config.GetSection("Email"));
        using var provider = services.BuildServiceProvider();
        var app = provider.GetRequiredService<IOptions<AppOptions>>().Value;
        var email = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
        Assert.Equal("Sample", app.Name);
        Assert.Equal("https://blog.example", app.BaseUrl);
        Assert.Equal("", email.ApiKey); // Empty disables email even when a legacy key exists.
        Assert.Equal("sender@example.com", email.FromAddress);
        Assert.Equal("Sender", email.FromName);
        Assert.Equal(8, config.GetValue<int>("Jobs:Concurrency"));
        Assert.Equal("local-keys", config["DataProtection:KeysPath"]);
        Assert.Equal("sample-salt", config["Analytics:Salt"]);
        Assert.Equal("node", config["Inertia:SsrExecutable"]);
        Assert.Equal("30 3 * * *", config["Cron:prune"]); // Missing aliases retain existing settings.
    }
}
