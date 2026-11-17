using System.Security.Claims;
using CarParker.Infrastructure;

namespace CarParker.Features.Bookings;

/// <summary>PDF receipts: generated per request and streamed as attachments, never stored. Authorization is the booking page's.</summary>
public static class BookingDocuments
{
    public static void MapBookingDocuments(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account").RequireAuthorization("verified");
        account.MapGet("/bookings/{id:guid}/receipt.pdf", async (Guid id, ReceiptsService svc, ClaimsPrincipal user, IConfiguration config, HttpContext ctx, CancellationToken ct) =>
        {
            var (receipt, failure) = await svc.BookingAsync(id, user.UserId(), user.IsAdmin(), ct);
            return receipt is null
                ? failure!.ToResult(ctx)
                : Pdf(ReceiptDocument.Render(receipt, AppName(config)), $"receipt-{Display.ShortId(id)}.pdf", ctx);
        }).WithName("account.bookings.receipt");
        account.MapGet("/bookings/{id:guid}/refund-receipt.pdf", async (Guid id, ReceiptsService svc, ClaimsPrincipal user, IConfiguration config, HttpContext ctx, CancellationToken ct) =>
        {
            var (receipt, failure) = await svc.RefundAsync(id, user.UserId(), user.IsAdmin(), ct);
            return receipt is null
                ? failure!.ToResult(ctx)
                : Pdf(ReceiptDocument.Render(receipt, AppName(config)), $"refund-receipt-{Display.ShortId(id)}.pdf", ctx);
        }).WithName("account.bookings.refund-receipt");
    }

    static string AppName(IConfiguration config) => config["App:Name"] is { Length: > 0 } name ? name : "CarParker";

    static IResult Pdf(byte[] bytes, string fileName, HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "private, no-store";
        return Results.File(bytes, "application/pdf", fileName);
    }
}
