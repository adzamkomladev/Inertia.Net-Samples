using Microsoft.AspNetCore.DataProtection;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Digests;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class DigestEndpoints
{
    public static void MapDigests(this IEndpointRouteBuilder app) =>
            app.MapGet("/email/unsubscribe/daily", UnsubscribeAsync).AllowAnonymous();

    static async Task<IResult> UnsubscribeAsync(string? token, IDataProtectionProvider protection, AppDb db, CancellationToken ct)
    {
        await DigestService.UnsubscribeAsync(token, protection, db, ct);
        return Results.Content("<!doctype html><html><body><h1>You're unsubscribed</h1><p>Daily recommendation emails are turned off.</p></body></html>", "text/html");
    }
}
