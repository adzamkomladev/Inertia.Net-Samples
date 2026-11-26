using KitchenSink.Data;
using KitchenSink.Features.Crm;
using KitchenSink.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Showcase;

public static class FormsFeature
{
    static readonly string[] Roles = ["developer", "designer", "manager", "other"];

    public static IEndpointRouteBuilder MapForms(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/forms").RequireAuthorization();

        group.MapGet("/use-form", () => Inertia.Net.Inertia.Render("Features/Forms/UseForm"));
        group.MapPost("/use-form", SubmitSimpleForm);
        group.MapGet("/form-component", () => Inertia.Net.Inertia.Render("Features/Forms/FormComponent"));
        group.MapPost("/form-component", SubmitSimpleForm);
        group.MapGet("/file-uploads", () => Inertia.Net.Inertia.Render("Features/Forms/FileUploads"));
        group.MapPost("/file-uploads", SubmitFileUploads);
        group.MapGet("/validation", () => Inertia.Net.Inertia.Render("Features/Forms/Validation"));
        group.MapPost("/validation", SubmitValidation);
        group.MapPost("/validation/secondary", SubmitValidationSecondary);
        group.MapGet("/precognition", () => Inertia.Net.Inertia.Render("Features/Forms/Precognition"));
        group.MapPost("/precognition", StoreAccount);
        group.MapGet("/optimistic-updates", OptimisticUpdates);
        group.MapPost("/optimistic-toggle/{contact:int}", ToggleFavorite);
        group.MapGet("/use-form-context", () => Inertia.Net.Inertia.Render("Features/Forms/UseFormContext"));
        group.MapGet("/dotted-keys", () => Inertia.Net.Inertia.Render("Features/Forms/DottedKeys"));
        group.MapPost("/dotted-keys", SubmitDottedKeys);
        group.MapGet("/wayfinder", Wayfinder);

        return endpoints;
    }

    static async Task<IResult> SubmitSimpleForm(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var name = input.String("name");
        var email = input.String("email");
        var bio = input.String("bio");
        var role = input.String("role");
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["The name field is required."];
        if (string.IsNullOrWhiteSpace(email)) errors["email"] = ["The email field is required."];
        else if (!InertiaForms.LooksLikeEmail(email)) errors["email"] = ["The email field must be a valid email address."];
        if (bio is { Length: > 1000 }) errors["bio"] = ["The bio field must not be greater than 1000 characters."];
        if (role is null || !Roles.Contains(role)) errors["role"] = ["The selected role is invalid."];

        if (errors.Count > 0)
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        return Inertia.Net.Inertia.Back()
            .WithFlash("message", $"Form submitted successfully! Name: {name}");
    }

    static async Task<IResult> SubmitFileUploads(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var photo = input.Files("photo");
        var files = input.Files("files");
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (photo.Length > 0 && photo[0].Length > 2 * 1024 * 1024)
            errors["photo"] = ["The photo field must not be greater than 2048 kilobytes."];
        if (files.Length > 5)
            errors["files"] = ["The files field must not have more than 5 items."];
        if (files.Any(f => f.Length > 5 * 1024 * 1024))
            errors["files"] = ["Each file must not be greater than 5120 kilobytes."];

        if (errors.Count > 0)
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        var count = files.Length + photo.Length;
        return Inertia.Net.Inertia.Back().WithFlash("message", $"Uploaded {count} file(s) successfully!");
    }

    static async Task<IResult> SubmitValidation(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var name = input.String("name");
        var email = input.String("email");
        var age = input.Int("age");
        var website = input.String("website");
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(name)) errors["name"] = "Please enter your full name.";
        if (string.IsNullOrWhiteSpace(email)) errors["email"] = "We need your email address.";
        else if (!InertiaForms.LooksLikeEmail(email)) errors["email"] = "That doesn't look like a valid email.";
        if (age is null) errors["age"] = "Please enter your age.";
        else if (age < 18) errors["age"] = "You must be at least 18 years old.";
        else if (age > 120) errors["age"] = "Please enter a valid age.";
        if (!string.IsNullOrWhiteSpace(website) && !Uri.TryCreate(website, UriKind.Absolute, out _))
            errors["website"] = "Please enter a valid URL (e.g., https://example.com).";

        if (errors.Count > 0)
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        return Inertia.Net.Inertia.Back().WithFlash("message", "Primary form submitted successfully!");
    }

    static async Task<IResult> SubmitValidationSecondary(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var title = input.String("title");
        var body = input.String("body");
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(title)) errors["title"] = "The title field is required.";
        if (string.IsNullOrWhiteSpace(body)) errors["body"] = "The body field is required.";
        else if (body.Length < 10) errors["body"] = "The body field must be at least 10 characters.";

        if (errors.Count > 0)
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        return Inertia.Net.Inertia.Back().WithFlash("message", "Secondary form submitted successfully!");
    }

    static async Task<IResult> StoreAccount(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var username = input.String("username");
        var email = input.String("email");
        var password = input.String("password");
        var confirmation = input.String("password_confirmation");

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(username)) errors["username"] = ["The username field is required."];
        else if (username.Length < 3) errors["username"] = ["The username field must be at least 3 characters."];
        else if (username.Length > 20) errors["username"] = ["The username field must not be greater than 20 characters."];
        else if (!username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            errors["username"] = ["The username field may only contain letters, numbers, dashes and underscores."];
        if (string.IsNullOrWhiteSpace(email)) errors["email"] = ["The email field is required."];
        else if (!InertiaForms.LooksLikeEmail(email)) errors["email"] = ["The email field must be a valid email address."];
        if (string.IsNullOrWhiteSpace(password)) errors["password"] = ["The password field is required."];
        else if (password.Length < 8) errors["password"] = ["The password field must be at least 8 characters."];
        if (confirmation != password) errors["password_confirmation"] = ["The password confirmation field must match password."];

        if (await Precognition.TryHandleAsync(context, () => errors) is { } precognitive)
        {
            return precognitive;
        }

        if (errors.Count > 0)
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        return Inertia.Net.Inertia.Back().WithFlash("message", $"Account created for {username}!");
    }

    static async Task<IResult> OptimisticUpdates(AppDb db, CancellationToken cancellationToken)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        return Inertia.Net.Inertia.Render("Features/Forms/OptimisticUpdates", new InertiaProps
        {
            ["contacts"] = contacts.Select(c => c.ToDto()).ToList(),
        });
    }

    static async Task<IResult> ToggleFavorite(
        int contact,
        HttpContext context,
        AppDb db,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        if (context.Request.Query["simulate_error"].ToString() is "1" or "true"
            || (await TryReadSimulateError(context, cancellationToken)))
        {
            return InertiaForms.BackWithErrors(context, new Dictionary<string, string>
            {
                ["contact"] = "Simulated validation error for optimistic update rollback demo.",
            });
        }

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
    }

    static async Task<bool> TryReadSimulateError(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            return input.Bool("simulate_error");
        }
        catch
        {
            return false;
        }
    }

    static async Task<IResult> SubmitDottedKeys(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var name = input.String("user.name");
        var email = input.String("user.email");
        var street = input.String("address.street");
        var city = input.String("address.city");
        var tags = input.Strings("tags").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(name)) errors["user.name"] = "The name field is required.";
        if (string.IsNullOrWhiteSpace(email)) errors["user.email"] = "The email field is required.";
        else if (!InertiaForms.LooksLikeEmail(email)) errors["user.email"] = "The email field must be a valid email address.";
        if (string.IsNullOrWhiteSpace(street)) errors["address.street"] = "The street field is required.";
        if (string.IsNullOrWhiteSpace(city)) errors["address.city"] = "The city field is required.";
        if (tags.Any(t => t.Length > 50)) errors["tags"] = "Each tag must not be greater than 50 characters.";

        if (errors.Count > 0)
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        var parsed = new
        {
            user = new { name, email },
            address = new { street, city },
            tags,
        };

        return Inertia.Net.Inertia.Back()
            .WithFlash("message", "Form submitted successfully!")
            .WithFlash("parsedData", parsed);
    }

    static async Task<IResult> Wayfinder(AppDb db, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.AsNoTracking()
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return contact is null
            ? Results.NotFound()
            : Inertia.Net.Inertia.Render("Features/Forms/Wayfinder", new InertiaProps
            {
                ["sampleContact"] = contact.ToDto(),
            });
    }
}
