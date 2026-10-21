namespace Inkwell.Features.Analytics;

public sealed record DayRow(string Date, int Views, int Likes, int Comments);

public sealed record SummaryDto(
    int Views,
    int Likes,
    int Comments,
    int Posts,
    int PrevViews,
    int PrevLikes,
    int PrevComments
);

public sealed record TopPostDto(
    Guid Id,
    string Title,
    int Views,
    int Likes,
    int Comments,
    int LifetimeViews
);

public sealed class DayCount
{
    public string Day { get; set; } = "";
    public int N { get; set; }
}

public sealed class TopPostRow
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public int Views { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int LifetimeViews { get; set; }
}

internal sealed record PostHeader(
    Guid Id,
    string Title,
    Guid AuthorId,
    DateTime CreatedAt,
    int ViewCount,
    int LikeCount,
    int CommentCount
);
