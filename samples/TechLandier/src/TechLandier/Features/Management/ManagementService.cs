using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

public sealed class ManagementService(AppDb db)
{
    public async Task<StoreSettingsView> SettingsAsync(CancellationToken ct)
    {
        var settings = await db.Set<StoreSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new StoreSettings();
        var hours = JsonSerializer.Deserialize<List<BusinessHours>>(settings.HoursJson) ?? [];
        if (hours.Count == 0) hours = Enumerable.Range(0, 7).Select(day => new BusinessHours(day, day == 0, "09:00", "18:00", day == 0, "10:00", "17:00")).ToList();
        return new(settings.Name, settings.Email, settings.Phone, settings.Address, settings.TimeZoneId, hours);
    }

    public async Task<SaveResult> SaveSettingsAsync(StoreSettingsView input, CancellationToken ct)
    {
        var error = ManagementValidators.SettingsError(input);
        if (error is not null) return SaveResult.Invalid("hours", error);
        var settings = await db.Set<StoreSettings>().FirstOrDefaultAsync(ct);
        if (settings is null) db.Add(settings = new StoreSettings());
        settings.Name = input.Name.Trim();
        settings.Email = input.Email.Trim().ToLowerInvariant();
        settings.Phone = input.Phone.Trim();
        settings.Address = input.Address.Trim();
        settings.TimeZoneId = input.TimeZoneId;
        settings.HoursJson = JsonSerializer.Serialize(input.Hours.OrderBy(h => h.Day));
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Store settings saved.");
    }

    public async Task<List<TeamMember>> TeamAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Role == "admin").OrderBy(u => u.Name)
            .Select(u => new TeamMember(u.Id, u.Name, u.Email, u.Role)).ToListAsync(ct);

    public async Task<SaveResult> CreateAdminAsync(TeamInput input, CancellationToken ct)
    {
        if (input.Name?.Trim() is not { Length: >= 2 and <= 80 } name) return SaveResult.Invalid("name", "Enter a name between 2 and 80 characters.");
        if (input.Email?.Trim().ToLowerInvariant() is not { Length: <= 254 } email || !new EmailAddressAttribute().IsValid(email)) return SaveResult.Invalid("email", "Enter a valid email.");
        if (input.Password is not { Length: >= 12 and <= 128 }) return SaveResult.Invalid("password", "Use a password between 12 and 128 characters.");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return SaveResult.Invalid("email", "This email already has an account. Existing accounts are not overwritten.");
        var user = new User { Name = name, Email = email, Role = "admin", EmailVerifiedAt = DateTime.UtcNow };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, input.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Administrator created.");
    }

    public async Task<SaveResult> RemoveAdminAsync(Guid id, Guid actor, CancellationToken ct)
    {
        if (id == actor) return SaveResult.Error("You cannot remove your own admin access.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "admin", ct);
        if (user is null) return SaveResult.Missing;
        user.Role = "customer";
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Admin access removed. Their customer account is preserved.");
    }
}
