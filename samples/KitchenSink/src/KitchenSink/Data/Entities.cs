namespace KitchenSink.Data;

public sealed class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Note> Notes { get; set; } = [];
}

public sealed class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<Contact> Contacts { get; set; } = [];
}

public sealed class Contact
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Note> Notes { get; set; } = [];

    public string Name => $"{FirstName} {LastName}";
}

public sealed class Note
{
    public int Id { get; set; }
    public string Body { get; set; } = "";
    public int ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class LiveVote
{
    public int Id { get; set; }
    public string Visitor { get; set; } = "";
    public string Framework { get; set; } = "";
}

public sealed class LiveReaction
{
    public int Id { get; set; }
    public string Visitor { get; set; } = "";
    public string Reaction { get; set; } = "";
}

public sealed class LiveTick
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
}
