using System.Security.Claims;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// PDF receipts and installment statements. Generated per request and streamed, never stored. A caller who may not
/// open a document gets the same 404 as a missing one.
public static class Documents
{
    public static void MapDocuments(this IEndpointRouteBuilder app)
    {
        // Guests follow the same unguessable token as /orders/{number}?t=; the signed-in owner needs none.
        app.MapGet("/orders/{number}/receipt.pdf", async (string number, string? t, ClaimsPrincipal user, DocumentsService docs, HttpContext http, CancellationToken ct) =>
            Receipt(await docs.ReceiptAsync(number, new(user.UserIdOrNull(), t), ct), http)).WithName("orders.receipt");

        app.MapGet("/account/orders/{number}/receipt.pdf", async (string number, ClaimsPrincipal user, DocumentsService docs, HttpContext http, CancellationToken ct) =>
            Receipt(await docs.ReceiptAsync(number, new(user.UserId()), ct), http))
            .RequireAuthorization("verified").WithName("account.orders.receipt");

        app.MapGet("/admin/orders/{number}/receipt.pdf", async (string number, DocumentsService docs, HttpContext http, CancellationToken ct) =>
            Receipt(await docs.ReceiptAsync(number, new(null, Admin: true), ct), http))
            .RequireAuthorization("admin").WithName("admin.orders.receipt");

        app.MapGet("/account/installments/{planId:guid}/statement.pdf", async (Guid planId, ClaimsPrincipal user, DocumentsService docs, HttpContext http, CancellationToken ct) =>
        {
            if (await docs.StatementAsync(planId, user.UserId(), ct) is not { } statement) return RouteResults.NotFoundPage();
            return Pdf(StatementDocument.Render(statement, DateTime.UtcNow), $"statement-{statement.OrderNumber}.pdf", http);
        }).RequireAuthorization("verified").WithName("account.installments.statement");
    }

    static IResult Receipt(ReceiptData? receipt, HttpContext http)
    {
        return receipt is null
            ? RouteResults.NotFoundPage()
            : Pdf(ReceiptDocument.Render(receipt), $"receipt-{receipt.ReceiptNumber}.pdf", http);
    }

    static IResult Pdf(byte[] bytes, string fileName, HttpContext http)
    {
        http.Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        http.Response.Headers.CacheControl = "private, no-store";
        return Results.Bytes(bytes, "application/pdf");
    }
}
