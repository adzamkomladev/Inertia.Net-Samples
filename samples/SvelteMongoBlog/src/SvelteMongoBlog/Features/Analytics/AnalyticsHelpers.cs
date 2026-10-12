using MongoDB.Bson;

namespace SvelteMongoBlog.Features;

public static partial class AnalyticsHelpers
{
    /// <summary>Raw pipelines compare against the Standard (subtype 4) binary the Guid serializer writes.</summary>
    internal static BsonValue Bin(Guid id) => new BsonBinaryData(id, GuidRepresentation.Standard);

    internal static int Days(int? days) => days is 7 or 30 or 90 ? days.Value : 30;

    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}

public static partial class AnalyticsService
{
    internal static DateTime Utc(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    internal static int Value(BsonDocument d, string name) =>
        d.TryGetValue(name, out var v) && v.IsNumeric ? v.ToInt32() : 0;
}
