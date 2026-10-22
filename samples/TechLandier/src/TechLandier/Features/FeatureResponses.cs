using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Maps plain service outcomes at the route boundary, including auth handlers.
public sealed class FeatureResponses : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var value = await next(context);
        if (value is not SaveResult result) return value;
        if (result.NotFound) return HttpExtensions.NotFoundPage();
        var toast = result.FlashType is null ? null : new Toast(result.FlashType, result.FlashMessage!);
        if (result.RedirectUrl is { } url)
        {
            if (toast is not null) context.HttpContext.Inertia().Flash("toast", toast);
            return Results.Redirect(url);
        }
        var back = Back();
        if (result.Errors is not null) back.WithErrors(result.Errors);
        else if (toast is not null) back.WithFlash("toast", toast);
        return back;
    }
}
