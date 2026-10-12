namespace VuePgReplicasAdvBlog.Features.Analytics;

public static class AnalyticsHelpers
{
    public static int Days(int? days) => days is 7 or 30 or 90 ? days.Value : 30;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
