using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Inertia.Net.Testing;

namespace KitchenSink.Tests;

[Collection("App")]
public sealed class FormEndpointTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Use_form_validates_and_flashes_the_submitted_name()
    {
        using (var invalid = await PostAsync("/features/forms/use-form", new { }))
        {
            Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        }

        var page = await PageAsync("/features/forms/use-form");
        new AssertableInertia(page).HasError("name").HasError("email");

        using (var valid = await PostAsync("/features/forms/use-form", new
        {
            name = "John Doe",
            email = "john@example.com",
            role = "developer",
            subscribe = true,
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        }

        var saved = await PageAsync("/features/forms/use-form");
        Assert.Equal("Form submitted successfully! Name: John Doe", saved.Flash["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Secondary_validation_errors_use_the_requested_bag()
    {
        using (var invalid = await PostAsync(
                   "/features/forms/validation/secondary",
                   new { title = "", body = "short" },
                   errorBag: "secondaryForm"))
        {
            Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        }

        var page = await PageAsync("/features/forms/validation");
        new AssertableInertia(page).HasError("title", bag: "secondaryForm");
    }

    [Fact]
    public async Task Dotted_keys_return_the_validated_structure_in_flash()
    {
        using (var invalid = await PostAsync("/features/forms/dotted-keys", new
        {
            user = new { name = "Ada", email = "not-an-email" },
            address = new { street = "1 Main", city = "" },
            tags = new[] { "one" },
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        }

        var errors = await PageAsync("/features/forms/dotted-keys");
        new AssertableInertia(errors)
            .HasError("user.email")
            .HasError("address.city");

        using (var valid = await PostAsync("/features/forms/dotted-keys", new
        {
            user = new { name = "Ada", email = "ada@example.com" },
            address = new { street = "1 Main", city = "Accra" },
            tags = new[] { "one", "two" },
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        }

        var page = await PageAsync("/features/forms/dotted-keys");
        var parsed = (JsonObject)page.Flash["parsedData"]!;
        Assert.Equal("Ada", ((JsonObject)parsed["user"]!)["name"]!.GetValue<string>());
        Assert.Equal("Accra", ((JsonObject)parsed["address"]!)["city"]!.GetValue<string>());
        Assert.Equal(2, parsed["tags"]!.AsArray().Count);
    }

    [Fact]
    public async Task Optimistic_toggle_can_simulate_a_rollback_error()
    {
        var contact = await App.SeedContactAsync();

        using (var response = await PostAsync(
                   $"/features/forms/optimistic-toggle/{contact.Id}",
                   new { simulate_error = true }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var page = await PageAsync("/features/forms/optimistic-updates");
        new AssertableInertia(page).HasError("contact");
    }

    [Fact]
    public async Task Form_component_accepts_a_urlencoded_submit()
    {
        using var page = await Client.GetAsync("/dashboard");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Form Component",
            ["email"] = "form@example.com",
            ["role"] = "developer",
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/features/forms/form-component") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Inertia", "true");
        request.Headers.TryAddWithoutValidation("X-Inertia-Version", AppFactory.Version);
        request.WithXsrfToken(page);

        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var after = await PageAsync("/features/forms/form-component");
        Assert.Equal("Form submitted successfully! Name: Form Component", after.Flash["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task File_uploads_count_the_attached_files()
    {
        using var page = await Client.GetAsync("/dashboard");
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1, 2, 3]), "files[]", "one.txt");
        content.Add(new ByteArrayContent([4, 5]), "files[]", "two.txt");
        content.Add(new ByteArrayContent([6]), "photo", "photo.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/features/forms/file-uploads") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Inertia", "true");
        request.Headers.TryAddWithoutValidation("X-Inertia-Version", AppFactory.Version);
        request.WithXsrfToken(page);

        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var after = await PageAsync("/features/forms/file-uploads");
        Assert.Equal("Uploaded 3 file(s) successfully!", after.Flash["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Use_http_api_returns_a_json_greeting()
    {
        using var response = await App.PostAsync(Client, "/features/http/use-http/api", new { name = "Ada" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hello, Ada!", body);
    }
}
