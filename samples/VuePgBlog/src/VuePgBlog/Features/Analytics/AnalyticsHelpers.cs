namespace VuePgBlog.Features;

public static partial class AnalyticsHelpers
{
    internal static int Days(int? days) => days is 7 or 30 or 90 ? days.Value : 30;

    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
