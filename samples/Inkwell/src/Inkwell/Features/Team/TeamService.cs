using System.ComponentModel.DataAnnotations;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Inkwell.Features.Team;
public sealed class TeamService(AppDb db, UserManager<AppUser> users)
{
    public async Task<List<TeamMember>> MembersAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.IsAdmin).OrderBy(u => u.DisplayName).Select(u => new TeamMember(u.Id, u.DisplayName, u.Email!)).ToListAsync(ct);
    public async Task<Outcome> CreateAsync(TeamInput input)
    {
        if (input.Name?.Trim() is not { Length: >= 2 and <= 80 } name) return Outcome.Fail("name", "Enter a name between 2 and 80 characters.");
        if (input.Email?.Trim().ToLowerInvariant() is not { Length: <= 254 } email || !new EmailAddressAttribute().IsValid(email)) return Outcome.Fail("email", "Enter a valid email.");
        if (input.Password is not { Length: >= 12 and <= 128 }) return Outcome.Fail("password", "Use a password between 12 and 128 characters.");
        if (await users.FindByEmailAsync(email) is not null) return Outcome.Fail("email", "This email already has an account.");
        var user = new AppUser { UserName = email, Email = email, DisplayName = name, IsAdmin = true, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
        var result = await users.CreateAsync(user, input.Password);
        return result.Succeeded ? Outcome.FlashBack("success", "Administrator created.") : Outcome.Fail("email", string.Join(" ", result.Errors.Select(e => e.Description)));
    }
    public async Task<Outcome> RemoveAsync(Guid id, Guid actor, CancellationToken ct)
    {
        if (id == actor) return Outcome.Fail("role", "You cannot remove your own admin access.");
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || !user.IsAdmin) return Outcome.Missing();
        user.IsAdmin = false;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? Outcome.FlashBack("success", "Admin access removed; author account preserved.") : Outcome.Fail("role", "Account changed. Reload and try again.");
    }
}
