using KitchenSink.Data;
using KitchenSink.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Crm;

public static class CrmFeature
{
    public static IEndpointRouteBuilder MapCrm(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("").RequireAuthorization();

        group.MapGet("/dashboard", (CrmService service, AppDb db, CancellationToken cancellationToken) =>
            Inertia.Net.Inertia.Render("Crm/Dashboard", new InertiaProps
            {
                ["totalContacts"] = Inertia.Net.Inertia.Defer(async ct => await db.Contacts.CountAsync(ct)),
                ["totalOrganizations"] = Inertia.Net.Inertia.Defer(async ct => await db.Organizations.CountAsync(ct)),
                ["recentNotesCount"] = Inertia.Net.Inertia.Defer(async ct =>
                    await db.Notes.CountAsync(n => n.CreatedAt >= DateTime.UtcNow.AddDays(-7), ct)),
                ["recentActivity"] = Inertia.Net.Inertia.Prop(async ct => await service.RecentNotesAsync(10, ct)),
            }));

        group.MapGet("/contacts", async (
            CrmService service,
            HttpRequest request,
            CancellationToken cancellationToken) =>
        {
            var search = request.Query["search"].ToString();
            var favorite = request.Query["favorite"].ToString() is "1" or "true";
            var cursor = request.Query["cursor"].ToString();
            var page = await service.ContactsAsync(search, favorite, cursor, cancellationToken: cancellationToken);

            return Inertia.Net.Inertia.Render("Contacts/Index", new InertiaProps
            {
                ["contacts"] = Inertia.Net.Inertia.Scroll(page.Value, "data", page.Metadata),
                ["filters"] = new { search, favorite },
            });
        });

        group.MapGet("/contacts/create", async (CrmService service, CancellationToken cancellationToken) =>
            Inertia.Net.Inertia.Render("Contacts/Create", new InertiaProps
            {
                ["organizations"] = await service.OrganizationOptionsAsync(cancellationToken),
            }));

        group.MapPost("/contacts", async (HttpContext context, AppDb db, CancellationToken cancellationToken) =>
        {
            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            var contact = new ContactInput
            {
                FirstName = input.String("first_name"),
                LastName = input.String("last_name"),
                Email = input.String("email"),
                Phone = input.String("phone"),
                OrganizationId = input.Int("organization_id"),
            };

            var errors = InertiaForms.ValidateContact(contact);
            if (contact.OrganizationId is { } organizationId
                && !await db.Organizations.AnyAsync(o => o.Id == organizationId, cancellationToken))
            {
                errors["organization_id"] = ["The selected organization is invalid."];
            }

            if (errors.Count > 0)
            {
                return InertiaForms.BackWithErrors(context, errors);
            }

            var entity = new Contact
            {
                FirstName = contact.FirstName!.Trim(),
                LastName = contact.LastName!.Trim(),
                Email = contact.Email?.Trim(),
                Phone = contact.Phone?.Trim(),
                OrganizationId = contact.OrganizationId,
                CreatedAt = DateTime.UtcNow,
            };
            db.Contacts.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            context.Inertia().Flash("message", "Contact created.");
            return Results.Redirect($"/contacts/{entity.Id}");
        });

        group.MapGet("/contacts/{contact:int}", async (
            int contact,
            CrmService service,
            AppDb db,
            CancellationToken cancellationToken) =>
        {
            var dto = await service.ContactAsync(contact, cancellationToken);
            if (dto is null)
            {
                return Results.NotFound();
            }

            return Inertia.Net.Inertia.Render("Contacts/Show", new InertiaProps
            {
                ["contact"] = dto,
                ["notes"] = Inertia.Net.Inertia.Defer(async ct => await service.NotesAsync(contact, ct)),
            });
        });

        group.MapGet("/contacts/{contact:int}/edit", async (
            int contact,
            CrmService service,
            CancellationToken cancellationToken) =>
        {
            var dto = await service.ContactAsync(contact, cancellationToken);
            if (dto is null)
            {
                return Results.NotFound();
            }

            return Inertia.Net.Inertia.Render("Contacts/Edit", new InertiaProps
            {
                ["contact"] = dto,
                ["organizations"] = await service.OrganizationOptionsAsync(cancellationToken),
            });
        });

        group.MapMethods("/contacts/{contact:int}", ["PUT", "PATCH"], async (
            int contact,
            HttpContext context,
            AppDb db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contact, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            var model = new ContactInput
            {
                FirstName = input.String("first_name"),
                LastName = input.String("last_name"),
                Email = input.String("email"),
                Phone = input.String("phone"),
                OrganizationId = input.Int("organization_id"),
            };

            var errors = InertiaForms.ValidateContact(model);
            if (model.OrganizationId is { } organizationId
                && !await db.Organizations.AnyAsync(o => o.Id == organizationId, cancellationToken))
            {
                errors["organization_id"] = ["The selected organization is invalid."];
            }

            if (errors.Count > 0)
            {
                return InertiaForms.BackWithErrors(context, errors);
            }

            entity.FirstName = model.FirstName!.Trim();
            entity.LastName = model.LastName!.Trim();
            entity.Email = model.Email?.Trim();
            entity.Phone = model.Phone?.Trim();
            entity.OrganizationId = model.OrganizationId;
            await db.SaveChangesAsync(cancellationToken);

            context.Inertia().Flash("message", "Contact updated.");
            return Results.Redirect($"/contacts/{entity.Id}");
        });

        group.MapDelete("/contacts/{contact:int}", async (
            int contact,
            HttpContext context,
            AppDb db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contact, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            db.Contacts.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            context.Inertia().Flash("message", "Contact deleted.");
            return Results.Redirect("/contacts");
        });

        group.MapPost("/contacts/{contact:int}/favorite", async (
            int contact,
            AppDb db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contact, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            entity.IsFavorite = !entity.IsFavorite;
            await db.SaveChangesAsync(cancellationToken);

            return Inertia.Net.Inertia.Back().WithFlash(
                "message",
                entity.IsFavorite ? "Added to favorites." : "Removed from favorites.");
        });

        group.MapPost("/contacts/{contact:int}/notes", async (
            int contact,
            HttpContext context,
            AppDb db,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Contacts.AnyAsync(c => c.Id == contact, cancellationToken))
            {
                return Results.NotFound();
            }

            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            var body = input.String("body");
            if (string.IsNullOrWhiteSpace(body))
            {
                return InertiaForms.BackWithErrors(context, new Dictionary<string, string[]>
                {
                    ["body"] = ["The body field is required."],
                });
            }

            db.Notes.Add(new Note
            {
                Body = body.Trim(),
                ContactId = contact,
                UserId = context.User.UserId() ?? 0,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);

            return Inertia.Net.Inertia.Back().WithFlash("message", "Note added.");
        });

        group.MapGet("/organizations", async (
            CrmService service,
            HttpRequest request,
            CancellationToken cancellationToken) =>
        {
            var search = request.Query["search"].ToString();
            var page = int.TryParse(request.Query["page"], out var number) ? number : 1;

            return Inertia.Net.Inertia.Render("Organizations/Index", new InertiaProps
            {
                ["organizations"] = await service.OrganizationsAsync(search, page, cancellationToken),
                ["filters"] = new { search },
            });
        });

        group.MapGet("/organizations/{organization:int}", async (
            int organization,
            CrmService service,
            CancellationToken cancellationToken) =>
        {
            var dto = await service.OrganizationAsync(organization, cancellationToken);
            if (dto is null)
            {
                return Results.NotFound();
            }

            var contacts = await service.ContactsAsync(
                search: null,
                favorite: false,
                cursor: null,
                organizationId: organization,
                cancellationToken: cancellationToken);

            return Inertia.Net.Inertia.Render("Organizations/Show", new InertiaProps
            {
                ["organization"] = dto,
                ["contacts"] = Inertia.Net.Inertia.Scroll(contacts.Value, "data", contacts.Metadata),
            });
        });

        group.MapMethods("/organizations/{organization:int}", ["PUT", "PATCH"], async (
            int organization,
            HttpContext context,
            AppDb db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organization, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            var name = input.String("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return InertiaForms.BackWithErrors(context, new Dictionary<string, string[]>
                {
                    ["name"] = ["The name field is required."],
                });
            }

            entity.Name = name.Trim();
            await db.SaveChangesAsync(cancellationToken);
            return Inertia.Net.Inertia.Back().WithFlash("message", "Organization updated.");
        });

        return endpoints;
    }
}
