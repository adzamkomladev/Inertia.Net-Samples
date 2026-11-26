using KitchenSink.Data;

namespace KitchenSink.Features.Crm;

public static class CrmMappers
{
    public static UserDto ToDto(this User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
    };

    public static SharedUserDto ToSharedDto(this User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        EmailVerifiedAt = user.EmailVerifiedAt,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };

    public static OrganizationDto ToDto(this Organization organization, bool withCount = false) => new()
    {
        Id = organization.Id,
        Name = organization.Name,
        ContactsCount = withCount ? organization.Contacts.Count : null,
        CreatedAt = organization.CreatedAt,
    };

    public static ContactDto ToDto(this Contact contact) => new()
    {
        Id = contact.Id,
        FirstName = contact.FirstName,
        LastName = contact.LastName,
        Email = contact.Email,
        Phone = contact.Phone,
        Organization = contact.Organization is null ? null : contact.Organization.ToDto(),
        IsFavorite = contact.IsFavorite,
        CreatedAt = contact.CreatedAt,
    };

    public static NoteDto ToDto(this Note note) => new()
    {
        Id = note.Id,
        Body = note.Body,
        Contact = note.Contact is null ? null : note.Contact.ToDto(),
        User = note.User?.ToDto(),
        CreatedAt = note.CreatedAt,
    };
}
