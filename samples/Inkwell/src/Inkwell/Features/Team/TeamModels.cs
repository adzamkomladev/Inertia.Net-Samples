namespace Inkwell.Features.Team;
public sealed record TeamInput(string? Name, string? Email, string? Password);
public sealed record TeamMember(Guid Id, string Name, string Email);
