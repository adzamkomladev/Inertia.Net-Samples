namespace ReactSqliteBlog.Features.Auth;

public static partial class AuthHelpers
{
    internal static bool IsLocal(string? url) =>
        Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url);
}
