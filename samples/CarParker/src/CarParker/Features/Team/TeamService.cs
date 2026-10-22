using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;
namespace CarParker.Features;
public sealed class TeamService(AppDb db)
{
    public async Task<TeamMember[]> MembersAsync(CancellationToken ct)
    {
        var users = await db.Users.Find(u => u.Role == UserRole.Admin || u.Role == UserRole.Guard).SortBy(u => u.Name).ToListAsync(ct);
        return users.Select(u => new TeamMember(u.Id, u.Name, u.Email, u.Role.ToString().ToLowerInvariant())).ToArray();
    }
    public async Task<Outcome> CreateAsync(TeamInput input, CancellationToken ct)
    {
        if (input.Role is not ("admin" or "guard")) return Outcome.Fail("role", "Choose admin or guard.");
        return await CreateAccountAsync(input, ct);
    }
    public async Task<Outcome> CreateCustomerAsync(TeamInput input, CancellationToken ct) =>
        await CreateAccountAsync(input with { Role = "customer" }, ct);

    async Task<Outcome> CreateAccountAsync(TeamInput input, CancellationToken ct)
    {
        if (input.Name?.Trim() is not { Length: >= 2 and <= 80 } name) return Outcome.Fail("name", "Enter a name between 2 and 80 characters.");
        if (input.Email?.Trim().ToLowerInvariant() is not { Length: <= 254 } email || !new EmailAddressAttribute().IsValid(email)) return Outcome.Fail("email", "Enter a valid email.");
        if (input.Password is not { Length: >= 12 and <= 128 }) return Outcome.Fail("password", "Use a password between 12 and 128 characters.");
        if (await db.Users.Find(u => u.Email == email).AnyAsync(ct)) return Outcome.Fail("email", "This email already has an account.");
        var user = new User { Name = name, Email = email, Role = input.Role == "admin" ? UserRole.Admin : input.Role == "guard" ? UserRole.Guard : UserRole.Customer, EmailVerifiedAt = input.Role == "customer" ? null : DateTime.UtcNow };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, input.Password);
        try { await db.Users.InsertOneAsync(user, cancellationToken: ct); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { return Outcome.Fail("email", "This email already has an account."); }
        return Outcome.FlashBack(new Toast("success", input.Role == "customer" ? "Customer created. They can verify their email when signing in." : "Team member created."));
    }
    public async Task<Outcome> RemoveAsync(Guid id, Guid actor, CancellationToken ct)
    {
        if (id == actor) return Outcome.Fail("role", "You cannot remove your own staff access.");
        var result = await db.Users.UpdateOneAsync(u => u.Id == id && u.Role != UserRole.Customer,
            Builders<User>.Update.Set(u => u.Role, UserRole.Customer), cancellationToken: ct);
        return result.MatchedCount == 0 ? Outcome.Missing() : Outcome.FlashBack(new Toast("success", "Staff access removed; customer account preserved."));
    }
}
