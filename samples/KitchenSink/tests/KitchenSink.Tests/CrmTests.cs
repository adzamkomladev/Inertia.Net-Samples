using System.Net;
using Inertia.Net.Testing;
using KitchenSink.Data;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Tests;

[Collection("App")]
public sealed class DashboardTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Dashboard_renders_with_deferred_stats()
    {
        var page = await PageAsync("/dashboard");

        Assert.Equal("Crm/Dashboard", page.Component);
        Assert.True(page.HasProp("recentActivity"));
        Assert.False(page.HasProp("totalContacts"));
        Assert.Contains("totalContacts", page.DeferredProps["default"]);
        Assert.Contains("totalOrganizations", page.DeferredProps["default"]);
        Assert.Contains("recentNotesCount", page.DeferredProps["default"]);

        var loaded = await page.LoadDeferredPropsAsync(Client);
        Assert.Equal(0, loaded.Prop("totalContacts")!.GetValue<int>());
        Assert.Equal(0, loaded.Prop("totalOrganizations")!.GetValue<int>());
        Assert.Equal(0, loaded.Prop("recentNotesCount")!.GetValue<int>());
    }

    [Fact]
    public async Task Recent_activity_is_limited_to_ten()
    {
        var contact = await App.SeedContactAsync();
        for (var i = 0; i < 15; i++)
        {
            await App.SeedNoteAsync(contact.Id, $"Note {i}");
        }

        var page = await PageAsync("/dashboard");
        Assert.Equal(10, page.Prop("recentActivity")!.AsArray().Count);
    }

    [Fact]
    public async Task Guests_are_redirected_to_login()
    {
        var anon = App.Client();
        using var response = await anon.InertiaGetAsync("/dashboard", AppFactory.Version);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", LocationPath(response));
    }
}

[Collection("App")]
public sealed class ContactTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Index_lists_contacts_and_filters_them()
    {
        await App.SeedContactAsync("Alice", "Smith", "alice@example.com");
        await App.SeedContactAsync("Bob", "Jones", "bob@example.com");
        await App.SeedContactAsync("Cara", "Jones", "cara@example.com", favorite: true);

        var page = await PageAsync("/contacts");
        Assert.Equal(3, page.Prop("contacts.data")!.AsArray().Count);

        var filtered = await PageAsync("/contacts?search=Alice");
        Assert.Single(filtered.Prop("contacts.data")!.AsArray());
        Assert.Equal("Alice", filtered.Prop("contacts.data.0.first_name")!.GetValue<string>());

        var favorites = await PageAsync("/contacts?favorite=1");
        Assert.Single(favorites.Prop("contacts.data")!.AsArray());
        Assert.Equal("Cara", favorites.Prop("contacts.data.0.first_name")!.GetValue<string>());
    }

    [Fact]
    public async Task Index_pages_the_cursor_after_fifteen_contacts()
    {
        for (var i = 0; i < 20; i++)
        {
            await App.SeedContactAsync($"Person{i:00}", "Test", $"person{i}@example.com");
        }

        var page = await PageAsync("/contacts");
        Assert.Equal(15, page.Prop("contacts.data")!.AsArray().Count);
        new AssertableInertia(page).HasScroll("contacts");

        var next = await page.LoadNextPageAsync(Client, "contacts");
        Assert.Equal(5, next.Prop("contacts.data")!.AsArray().Count);
    }

    [Fact]
    public async Task Create_form_lists_organizations()
    {
        await App.SeedOrganizationAsync("Acme");
        await App.SeedOrganizationAsync("Globex");

        var page = await PageAsync("/contacts/create");
        Assert.Equal("Contacts/Create", page.Component);
        Assert.Equal(2, page.Prop("organizations")!.AsArray().Count);
    }

    [Fact]
    public async Task Contact_can_be_created_updated_and_deleted()
    {
        var organization = await App.SeedOrganizationAsync();

        using (var stored = await PostAsync("/contacts", new
        {
            first_name = "John",
            last_name = "Doe",
            email = "john@example.com",
            phone = "555-1234",
            organization_id = organization.Id,
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, stored.StatusCode);
        }

        var contact = await App.DbAsync(db => db.Contacts.SingleAsync(c => c.FirstName == "John"));
        Assert.Equal("Doe", contact.LastName);
        Assert.Equal(organization.Id, contact.OrganizationId);

        var show = await PageAsync($"/contacts/{contact.Id}");
        Assert.Equal("Contacts/Show", show.Component);
        Assert.Equal(contact.Id, show.Prop("contact.id")!.GetValue<int>());

        var edit = await PageAsync($"/contacts/{contact.Id}/edit");
        Assert.Equal("Contacts/Edit", edit.Component);
        Assert.True(edit.HasProp("organizations"));

        using (var updated = await PostAsync($"/contacts/{contact.Id}", new
        {
            first_name = "Updated",
            last_name = "Name",
            email = "updated@example.com",
        }, HttpMethod.Put))
        {
            Assert.True(updated.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        }

        await App.DbAsync(async db =>
        {
            var fresh = await db.Contacts.AsNoTracking().SingleAsync(c => c.Id == contact.Id);
            Assert.Equal("Updated", fresh.FirstName);
            Assert.Equal("updated@example.com", fresh.Email);
        });

        using (var deleted = await PostAsync($"/contacts/{contact.Id}", method: HttpMethod.Delete))
        {
            Assert.True(deleted.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        }

        var count = await App.DbAsync(db => db.Contacts.CountAsync(c => c.Id == contact.Id));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Contact_validation_errors_come_back_on_the_form()
    {
        using (var response = await PostAsync("/contacts", new { }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var page = await PageAsync("/contacts/create");
        new AssertableInertia(page)
            .HasError("first_name")
            .HasError("last_name");
    }

    [Fact]
    public async Task Invalid_email_is_rejected()
    {
        using (var response = await PostAsync("/contacts", new
        {
            first_name = "John",
            last_name = "Doe",
            email = "not-an-email",
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var page = await PageAsync("/contacts/create");
        new AssertableInertia(page).HasError("email");
    }

    [Fact]
    public async Task Favorite_toggles_on_and_off()
    {
        var contact = await App.SeedContactAsync(favorite: false);

        using (var first = await PostAsync($"/contacts/{contact.Id}/favorite"))
        {
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        }

        Assert.True(await App.DbAsync(db => db.Contacts.Where(c => c.Id == contact.Id).Select(c => c.IsFavorite).SingleAsync()));

        using (var second = await PostAsync($"/contacts/{contact.Id}/favorite"))
        {
            Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        }

        Assert.False(await App.DbAsync(db => db.Contacts.Where(c => c.Id == contact.Id).Select(c => c.IsFavorite).SingleAsync()));
    }

    [Fact]
    public async Task All_contact_routes_require_authentication()
    {
        var contact = await App.SeedContactAsync();
        var anon = App.Client();

        var urls = new[]
        {
            "/contacts",
            "/contacts/create",
            $"/contacts/{contact.Id}",
            $"/contacts/{contact.Id}/edit",
        };

        foreach (var url in urls)
        {
            using var response = await anon.InertiaGetAsync(url, AppFactory.Version);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/login", LocationPath(response));
        }
    }
}

[Collection("App")]
public sealed class NoteTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task A_note_is_created_for_the_authenticated_user()
    {
        var contact = await App.SeedContactAsync();

        using (var response = await PostAsync($"/contacts/{contact.Id}/notes", new { body = "This is a test note." }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        await App.DbAsync(async db =>
        {
            var note = await db.Notes.AsNoTracking().SingleAsync(n => n.ContactId == contact.Id);
            Assert.Equal("This is a test note.", note.Body);
            Assert.Equal(contact.Id, note.ContactId);
            Assert.True(note.UserId > 0);
        });
    }

    [Fact]
    public async Task A_note_body_is_required()
    {
        var contact = await App.SeedContactAsync();

        using (var response = await PostAsync($"/contacts/{contact.Id}/notes", new { body = "" }))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var page = await PageAsync($"/contacts/{contact.Id}");
        new AssertableInertia(page).HasError("body");
    }

    [Fact]
    public async Task Notes_are_deferred_on_the_contact_page()
    {
        var contact = await App.SeedContactAsync();
        await App.SeedNoteAsync(contact.Id);

        var page = await PageAsync($"/contacts/{contact.Id}");
        Assert.False(page.HasProp("notes"));
        new AssertableInertia(page).HasDeferred("notes");

        var loaded = await page.LoadDeferredPropsAsync(Client);
        Assert.Single(loaded.Prop("notes")!.AsArray());
    }
}

[Collection("App")]
public sealed class OrganizationTests(AppFactory app) : AppTest(app)
{
    [Fact]
    public async Task Index_lists_searches_and_paginates_organizations()
    {
        await App.SeedOrganizationAsync("Acme Corp");
        await App.SeedOrganizationAsync("Globex Inc");
        await App.SeedOrganizationAsync("Initech");

        var page = await PageAsync("/organizations");
        Assert.Equal("Organizations/Index", page.Component);
        Assert.Equal(3, page.Prop("organizations.data")!.AsArray().Count);

        var filtered = await PageAsync("/organizations?search=Acme");
        Assert.Single(filtered.Prop("organizations.data")!.AsArray());
        Assert.Equal("Acme Corp", filtered.Prop("organizations.data.0.name")!.GetValue<string>());

        for (var i = 0; i < 25; i++)
        {
            await App.SeedOrganizationAsync($"Filler {i:00}");
        }

        var paged = await PageAsync("/organizations");
        Assert.Equal(20, paged.Prop("organizations.data")!.AsArray().Count);
        Assert.True(paged.Prop("organizations.meta.links")!.AsArray().Count > 3);
    }

    [Fact]
    public async Task Contact_count_and_members_are_shown()
    {
        var organization = await App.SeedOrganizationAsync();
        await App.SeedContactAsync("Alice", "Wonder", organizationId: organization.Id);
        await App.SeedContactAsync("Bob", "Builder", organizationId: organization.Id);
        await App.SeedContactAsync("Cara", "Jones", organizationId: organization.Id);

        var index = await PageAsync("/organizations");
        Assert.Equal(3, index.Prop("organizations.data.0.contacts_count")!.GetValue<int>());

        var show = await PageAsync($"/organizations/{organization.Id}");
        Assert.Equal("Organizations/Show", show.Component);
        Assert.Equal(organization.Id, show.Prop("organization.id")!.GetValue<int>());
        Assert.Equal(3, show.Prop("contacts.data")!.AsArray().Count);
    }

    [Fact]
    public async Task Organization_can_be_updated()
    {
        var organization = await App.SeedOrganizationAsync("Old Name");

        using (var response = await PostAsync($"/organizations/{organization.Id}", new { name = "New Name" }, HttpMethod.Put))
        {
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        }

        Assert.Equal("New Name", await App.DbAsync(db => db.Organizations.Where(o => o.Id == organization.Id).Select(o => o.Name).SingleAsync()));
    }

    [Fact]
    public async Task Organization_name_is_required()
    {
        var organization = await App.SeedOrganizationAsync();

        using (var response = await PostAsync($"/organizations/{organization.Id}", new { name = "" }, HttpMethod.Put))
        {
            Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        }

        var page = await PageAsync($"/organizations/{organization.Id}");
        new AssertableInertia(page).HasError("name");
    }

    [Fact]
    public async Task Organization_routes_require_authentication()
    {
        var organization = await App.SeedOrganizationAsync();
        var anon = App.Client();

        foreach (var url in new[] { "/organizations", $"/organizations/{organization.Id}" })
        {
            using var response = await anon.InertiaGetAsync(url, AppFactory.Version);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
    }
}
