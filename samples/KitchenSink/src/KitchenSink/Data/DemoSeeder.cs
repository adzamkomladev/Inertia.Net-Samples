using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Data;

public static class DemoSeeder
{
    public const string DemoEmail = "test@example.com";
    public const string DemoPassword = "password";

    public static string Hash(string password, User user) =>
        new PasswordHasher<User>().HashPassword(user, password);

    public static bool Verify(string hash, User user, string password) =>
        new PasswordHasher<User>().VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;

    /// <summary>Creates the shared demo login and CRM data when the database is empty.</summary>
    public static async Task EnsureSeededAsync(AppDb db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        await SeedAsync(db, cancellationToken);
    }

    /// <summary>Replaces the CRM demo data, keeping the shared demo login and its sessions.</summary>
    public static async Task<DateTime?> ResetAsync(AppDb db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Notes.ExecuteDeleteAsync(cancellationToken);
        await db.Contacts.ExecuteDeleteAsync(cancellationToken);
        await db.Organizations.ExecuteDeleteAsync(cancellationToken);
        await db.LiveVotes.ExecuteDeleteAsync(cancellationToken);
        await db.LiveReactions.ExecuteDeleteAsync(cancellationToken);
        await db.LiveTicks.ExecuteDeleteAsync(cancellationToken);
        await db.Users.Where(u => u.Email != DemoEmail).ExecuteDeleteAsync(cancellationToken);

        await SeedAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ResetAtAsync(db, cancellationToken);
    }

    public static async Task<DateTime?> ResetAtAsync(AppDb db, CancellationToken cancellationToken = default) =>
        await db.Organizations.OrderBy(o => o.CreatedAt).Select(o => (DateTime?)o.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    static async Task SeedAsync(AppDb db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var faker = new Faker();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == DemoEmail, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Name = "Test User",
                Email = DemoEmail,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            user.PasswordHash = Hash(DemoPassword, user);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        var organizations = new List<Organization>();
        for (var i = 0; i < 15; i++)
        {
            organizations.Add(new Organization
            {
                Name = $"{faker.Company.CompanyName()} {i + 1}",
                CreatedAt = now,
            });
        }

        db.Organizations.AddRange(organizations);
        await db.SaveChangesAsync(cancellationToken);

        var contacts = new List<Contact>();
        for (var i = 0; i < 75; i++)
        {
            contacts.Add(new Contact
            {
                FirstName = faker.Name.FirstName(),
                LastName = faker.Name.LastName(),
                Email = $"contact{i}@example.test",
                Phone = faker.Phone.PhoneNumber(),
                OrganizationId = organizations[i % organizations.Count].Id,
                IsFavorite = Random.Shared.Next(1, 101) <= 20,
                CreatedAt = now,
            });
        }

        for (var i = 0; i < 25; i++)
        {
            contacts.Add(new Contact
            {
                FirstName = faker.Name.FirstName(),
                LastName = faker.Name.LastName(),
                Email = $"contact{i + 75}@example.test",
                Phone = faker.Phone.PhoneNumber(),
                OrganizationId = null,
                IsFavorite = Random.Shared.Next(1, 101) <= 20,
                CreatedAt = now,
            });
        }

        db.Contacts.AddRange(contacts);
        await db.SaveChangesAsync(cancellationToken);

        var noted = contacts.OrderBy(_ => Random.Shared.Next()).Take(40).ToArray();
        foreach (var contact in noted)
        {
            var count = Random.Shared.Next(1, 6);
            for (var i = 0; i < count; i++)
            {
                db.Notes.Add(new Note
                {
                    Body = faker.Lorem.Paragraph(),
                    ContactId = contact.Id,
                    UserId = user.Id,
                    CreatedAt = now.AddMinutes(-Random.Shared.Next(0, 7 * 24 * 60)),
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
