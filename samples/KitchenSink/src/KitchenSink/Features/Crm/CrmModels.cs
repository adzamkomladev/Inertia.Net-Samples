using System.Text.Json.Serialization;

namespace KitchenSink.Features.Crm;

public sealed class UserDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("email")] public string Email { get; init; } = "";
}

public sealed class AuthDto
{
    [JsonPropertyName("user")] public SharedUserDto? User { get; init; }
}

public sealed class SharedUserDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("email")] public string Email { get; init; } = "";
    [JsonPropertyName("email_verified_at")] public DateTime? EmailVerifiedAt { get; init; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; init; }
}

public sealed class OrganizationDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("contacts_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContactsCount { get; init; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
}

public sealed class ContactDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; init; } = "";
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }

    [JsonPropertyName("organization")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrganizationDto? Organization { get; init; }

    [JsonPropertyName("is_favorite")] public bool IsFavorite { get; init; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
}

public sealed class NoteDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("body")] public string Body { get; init; } = "";

    [JsonPropertyName("contact")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactDto? Contact { get; init; }

    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UserDto? User { get; init; }

    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
}

public sealed class CursorData<T>
{
    [JsonPropertyName("data")] public List<T> Data { get; init; } = [];
}

public sealed class PaginationLink
{
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("label")] public string Label { get; init; } = "";
    [JsonPropertyName("active")] public bool Active { get; init; }
}

public sealed class PaginationMeta
{
    [JsonPropertyName("current_page")] public int CurrentPage { get; init; }
    [JsonPropertyName("last_page")] public int LastPage { get; init; }
    [JsonPropertyName("links")] public List<PaginationLink> Links { get; init; } = [];
}

public sealed class Paginated<T>
{
    [JsonPropertyName("data")] public List<T> Data { get; init; } = [];
    [JsonPropertyName("meta")] public PaginationMeta Meta { get; init; } = new();
}

public sealed class ContactInput
{
    [JsonPropertyName("first_name")] public string? FirstName { get; init; }
    [JsonPropertyName("last_name")] public string? LastName { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("phone")] public string? Phone { get; init; }
    [JsonPropertyName("organization_id")] public int? OrganizationId { get; init; }
}

public sealed class OrganizationInput
{
    [JsonPropertyName("name")] public string? Name { get; init; }
}

public sealed class NoteInput
{
    [JsonPropertyName("body")] public string? Body { get; init; }
}
