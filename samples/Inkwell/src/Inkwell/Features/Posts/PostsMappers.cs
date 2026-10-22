namespace Inkwell.Features.Posts;

internal static class PostsMappers
{
    public static PostCardDto ToCard(CardRow r) =>
        new(r.Id, r.Title, Search.Excerpt(r.Body), null, r.CoverUrl, r.CreatedAt, r.Author, r.LikeCount, r.CommentCount, r.ViewCount)
        {
            ArchivedAt = r.ArchivedAt,
        };

}
