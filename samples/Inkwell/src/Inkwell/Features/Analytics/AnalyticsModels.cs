using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.EntityFrameworkCore;

using Inertia.Net;

namespace Inkwell.Features.Analytics;

/// <summary>
/// Live events on the per-user channel: the analytics dashboards refresh while they are open, after the post changes
/// that invalidate their cache entries. The same definition names the prop side and the publish side.
/// </summary>
public static class AnalyticsEvents
{
    public static LiveEvent Changed(Guid userId) => new("AnalyticsChanged", LiveChannel.Private($"users.{userId}"));
}

public sealed record DayRow(string Date, int Views, int Likes, int Comments);

public sealed record SummaryDto(int Views, int Likes, int Comments, int Posts, int PrevViews, int PrevLikes, int PrevComments);

public sealed record TopPostDto(Guid Id, string Title, int Views, int Likes, int Comments, int LifetimeViews);

public sealed record PostHeader(Guid Id, string Title, Guid AuthorId, DateTime CreatedAt, int ViewCount, int LikeCount, int CommentCount);

/// <summary>The HTTP-independent facts a view beacon contributes to analytics.</summary>
public sealed record ViewVisit(Guid? UserId, string? IpAddress, string UserAgent);

public sealed record PostViewed(Guid PostId, string VisitorHash, string Day, Guid? ViewerId);
