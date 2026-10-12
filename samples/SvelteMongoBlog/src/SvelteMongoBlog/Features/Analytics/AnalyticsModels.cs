namespace SvelteMongoBlog.Features;

public sealed record Kpis(int Views, int UniqueViewers, int Likes, int Comments, int Posts);

public sealed record SeriesPoint(
    string Date,
    int Views,
    int UniqueViewers,
    int Likes,
    int Comments
);

public sealed record TopPost(
    Guid Id,
    string Slug,
    string Title,
    int Views,
    int Likes,
    int Comments
);

public sealed record Referrer(string Host, int Views);
