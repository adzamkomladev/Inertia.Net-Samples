namespace Inkwell.Features.Settings;

public static partial class SettingsHelpers
{
    internal static void SetThemeCookie(HttpContext ctx, string theme) =>
        ctx.Response.Cookies.Append(
            "theme",
            theme,
            new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                Secure = ctx.Request.IsHttps,
            }
        );
}
