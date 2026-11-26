using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KitchenSink.Features.Crm;
using Microsoft.AspNetCore.Http;

namespace KitchenSink.Infrastructure;

/// <summary>
/// Reads either an Inertia JSON body or a multipart/url-encoded form into one lookup.
/// Inertia sends JSON for ordinary useForm/Form submissions and multipart only when files are attached.
/// </summary>
public sealed class RequestInput
{
    readonly JsonObject _json;
    readonly IFormCollection _form;

    RequestInput(JsonObject json, IFormCollection form)
    {
        _json = json;
        _form = form;
    }

    public static async Task<RequestInput> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.HasFormContentType)
        {
            return new RequestInput(new JsonObject(), await request.ReadFormAsync(cancellationToken));
        }

        if (request.ContentLength is 0 or null && !request.Headers.ContainsKey("Content-Type"))
        {
            return new RequestInput(new JsonObject(), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));
        }

        try
        {
            var node = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken);
            return new RequestInput(node as JsonObject ?? new JsonObject(), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));
        }
        catch (JsonException)
        {
            return new RequestInput(new JsonObject(), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));
        }
    }

    public string? String(string name)
    {
        if (_form.TryGetValue(name, out var formValue))
        {
            var text = formValue.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        JsonNode? node = _json;
        foreach (var segment in name.Split('.'))
        {
            node = node?[segment];
        }

        if (node is null)
        {
            return null;
        }

        var value = node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node.ToString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public int? Int(string name)
    {
        var value = String(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    public bool Bool(string name)
    {
        var value = String(name);
        return value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    public List<string> Strings(string name)
    {
        var values = new List<string>();

        if (_json[name] is JsonArray array)
        {
            foreach (var item in array)
            {
                var text = item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : item?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text);
                }
            }
        }
        else if (_form.TryGetValue(name + "[]", out var formValues))
        {
            values.AddRange(formValues.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!));
        }
        else if (_form.TryGetValue(name, out var formValue))
        {
            values.AddRange(formValue.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!));
        }

        return values;
    }

    public IFormFile[] Files(string name)
    {
        var files = _form.Files.GetFiles(name);
        // Inertia serializes arrays with brackets: files[]=..., files[]=...
        return files.Count > 0 ? files.ToArray() : _form.Files.GetFiles(name + "[]").ToArray();
    }
}

public static class InertiaForms
{
    public static string Bag(HttpRequest request) =>
        request.Headers["X-Inertia-Error-Bag"].ToString() is { Length: > 0 } bag ? bag : "default";

    public static IResult BackWithErrors(HttpContext context, Dictionary<string, string[]> errors) =>
        Inertia.Net.Inertia.Back().WithErrors(errors, Bag(context.Request));

    public static IResult BackWithErrors(HttpContext context, Dictionary<string, string> errors) =>
        Inertia.Net.Inertia.Back().WithErrors(errors, Bag(context.Request));

    public static Dictionary<string, string[]> Required(params (string Field, string? Value)[] fields)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (field, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[field] = [$"The {field.Replace('_', ' ')} field is required."];
            }
        }

        return errors;
    }

    public static Dictionary<string, string[]> ValidateContact(ContactInput input)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(input.FirstName))
            errors["first_name"] = ["The first name field is required."];
        if (string.IsNullOrWhiteSpace(input.LastName))
            errors["last_name"] = ["The last name field is required."];
        if (!string.IsNullOrWhiteSpace(input.Email) && !LooksLikeEmail(input.Email))
            errors["email"] = ["The email field must be a valid email address."];

        return errors;
    }

    public static bool LooksLikeEmail(string value)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(value);
            return address.Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Laravel-style Precognition responses: validate first, answer 204/422 without running the handler,
/// and leave the real submit untouched when the header is absent.
/// </summary>
public static class Precognition
{
    public static bool IsPrecognitive(HttpRequest request) =>
        request.Headers["Precognition"].ToString() is { Length: > 0 } value && value != "false";

    public static string[] ValidateOnly(HttpRequest request) =>
        request.Headers["Precognition-Validate-Only"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Task<IResult?> TryHandleAsync(
        HttpContext context,
        Func<Dictionary<string, string[]>> validate)
    {
        if (!IsPrecognitive(context.Request))
        {
            return Task.FromResult<IResult?>(null);
        }

        context.Response.Headers["Precognition"] = "true";
        var only = ValidateOnly(context.Request);
        var errors = validate();

        if (only.Length > 0)
        {
            errors = errors
                .Where(e => only.Contains(e.Key, StringComparer.Ordinal))
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }

        if (errors.Count == 0)
        {
            context.Response.Headers["Precognition-Success"] = "true";
            return Task.FromResult<IResult?>(Results.NoContent());
        }

        return Task.FromResult<IResult?>(Results.Json(new
        {
            message = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "The given data was invalid.",
            errors,
        }, statusCode: StatusCodes.Status422UnprocessableEntity));
    }
}
