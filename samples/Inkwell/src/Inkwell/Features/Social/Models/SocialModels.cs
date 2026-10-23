using System.Text;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Social;

public sealed record UserSummary(Guid Id, string Name, string? AvatarUrl, string? Bio, DateTime JoinedAt, bool Following, int FollowerCount, int FollowingCount, bool IsSelf);

public sealed record UserPage(List<UserSummary> Users, string? Next);
