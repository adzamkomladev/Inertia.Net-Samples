using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Infrastructure;

public sealed class FeatureErrorFilter(bool json = false) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (FeatureException error)
        {
            return error.Kind switch
            {
                FailureKind.Validation when json => Results.ValidationProblem(error.Errors, statusCode: 422),
                FailureKind.Validation => Back().WithErrors(error.Errors.ToDictionary(pair => pair.Key, pair => pair.Value[0])),
                FailureKind.InvalidInput => Results.BadRequest(error.Message),
                FailureKind.Missing when json => Results.NotFound(),
                FailureKind.Missing => Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404),
                FailureKind.AccessDenied => Results.StatusCode(403),
                FailureKind.Expired => Results.StatusCode(410),
                _ => throw new InvalidOperationException("Unknown feature failure.", error)
            };
        }
    }
}
