using Microsoft.AspNetCore.DataProtection;
using ReactSqliteReplicasBlog.Data;

namespace ReactSqliteReplicasBlog.Infrastructure;

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
                    TimeProvider time,
                    CancellationToken ct
                ) =>
                    await DigestService.UnsubscribeAsync(token, dataProtection, db, time, ct)
                        ? Results.Content(
                            "<!doctype html><html><body><h1>You're unsubscribed</h1><p>Daily recommendation emails are turned off.</p></body></html>",
                            "text/html"
                        )
                        : Results.BadRequest("This unsubscribe link is invalid or expired.")
            )
            .AllowAnonymous();
}
