using System.ComponentModel.DataAnnotations;
namespace CarParker.Features;
public sealed record TeamInput(string? Name, string? Email, string? Password, string? Role);
public sealed record TeamMember(Guid Id, string Name, string Email, string Role);
