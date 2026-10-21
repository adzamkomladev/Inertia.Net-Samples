using Microsoft.EntityFrameworkCore;
using ReactSqliteBlog.Data;
using ReactSqliteBlog.Features.Posts;

namespace ReactSqliteBlog.Features.Social;

public static partial class SocialService
{
    internal static Task<List<UserSummary>> UsersAsync(
        AppDb db,
        Guid[] ids,
        Guid? uid,
        CancellationToken ct
    ) =>
        db
            .Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.Id)
            .Select(u => new UserSummary(
                u.Id,
                u.DisplayName,
                u.AvatarUrl,
                db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == u.Id),
                db.Follows.Count(f => f.FolloweeId == u.Id),
                db.Follows.Count(f => f.FollowerId == u.Id),
                uid == u.Id
            ))
            .ToListAsync(ct);

    internal static async Task<List<PostCardDto>> AddReposters(
        AppDb db,
        List<PostCardDto> items,
        Guid uid,
        CancellationToken ct
    )
    {
        var ids = items.Select(p => p.Id).ToArray();
        var rows = await (
            from r in db.Reposts.AsNoTracking()
            join u in db.Users on r.UserId equals u.Id
            where
                ids.Contains(r.PostId)
                && (
                    r.UserId == uid
                    || db.Follows.Any(f => f.FollowerId == uid && f.FolloweeId == r.UserId)
                )
            orderby r.CreatedAt descending
            select new
            {
                r.PostId,
                u.Id,
                u.DisplayName,
                u.AvatarUrl,
            }
        ).ToListAsync(ct);
        var byId = rows.GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.First());
        return items
            .Select(p =>
                byId.TryGetValue(p.Id, out var r)
                    ? p with
                    {
                        Reposter = new AuthorDto(r.Id, r.DisplayName, r.AvatarUrl),
                    }
                    : p
            )
            .ToList();
    }
}
