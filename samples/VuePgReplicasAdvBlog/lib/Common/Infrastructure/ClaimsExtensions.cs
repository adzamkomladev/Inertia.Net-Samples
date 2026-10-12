using System.Security.Claims;

namespace VuePgReplicasAdvBlog.Infrastructure;

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in."));

    public static Guid? UserIdOrNull(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
