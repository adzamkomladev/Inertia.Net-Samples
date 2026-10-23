using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using CarParker.Features.Alerts;
using CarParker.Features.Analytics;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;

namespace CarParker.Data;

public enum UserRole { Customer, Admin, Guard }

public sealed class User
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string Name { get; set; }
    public string PasswordHash { get; set; } = "";
    public string? Phone { get; set; }
    [BsonRepresentation(BsonType.String)] public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime? EmailVerifiedAt { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    /// <summary>Saved number plates offered at checkout; the default one is prefilled.</summary>
    public List<SavedVehicle> Vehicles { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    [BsonIgnoreIfNull]
    public string? DemoKey { get; set; }
}

public sealed class SavedVehicle
{
    public required string Plate { get; set; }
    [BsonIgnoreIfNull] public string? Label { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "system";
    public bool BookingReminders { get; set; } = true;
    public bool EmailOnCheckout { get; set; } = true;
    public bool EmailOnRefund { get; set; } = true;
}
