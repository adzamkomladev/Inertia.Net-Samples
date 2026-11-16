using System.Text;
using Inertia.Net;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Features.Social;

public sealed record UserSummary(Guid Id, string Name, string? AvatarUrl, string? Bio, DateTime JoinedAt, bool Following, int FollowerCount, int FollowingCount, bool IsSelf);

// Page records: the component name is declared once and the generated TypeScript type follows these props.
public sealed record SearchFilters(string Q);

public sealed record FeedIndexPage(
    [property: InertiaPropType(typeof(CursorPage<PostCardDto>))] InertiaProp Posts,
    string Mode,
    string Title,
    SearchFilters Filters) : IInertiaPage
{
    public static string Component => "Feed/Index";
}

public sealed record UsersIndexPage(
    [property: InertiaPropType(typeof(CursorPage<UserSummary>))] InertiaProp Users,
    SearchFilters Filters) : IInertiaPage
{
    public static string Component => "Users/Index";
}

