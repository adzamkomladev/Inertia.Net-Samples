using Microsoft.AspNetCore.DataProtection;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Infrastructure;

public static class UnsubscribeEndpoint
{
    /// <summary>Anonymous, signed unsubscribe endpoint scoped to the daily recommendation preference.</summary>
    public static void MapUnsubscribe(IEndpointRouteBuilder app) =>
        app.MapGet(
                "/email/unsubscribe/daily",
                async Task<IResult> (
                    string? token,
                    IDataProtectionProvider dataProtection,
                    AppDb db,
                    CancellationToken ct
                ) =>
                    await DigestService.UnsubscribeAsync(token, dataProtection, db, ct)
                        ? Results.Content(
                            "<!doctype html><html><body><h1>You're unsubscribed</h1><p>Daily recommendation emails are turned off.</p></body></html>",
                            "text/html"
                        )
                        : Results.BadRequest("This unsubscribe link is invalid or expired.")
            )
            .AllowAnonymous();
}
