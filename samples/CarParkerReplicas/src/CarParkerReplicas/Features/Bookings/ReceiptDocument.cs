using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CarParker.Infrastructure;
using CarParker.Features.Lots;
using static CarParker.Infrastructure.Display;
using P = CarParker.Infrastructure.BrandPalette;

namespace CarParker.Features.Bookings;

/// <summary>
/// Booking and refund receipt PDFs, styled like the emails (same palette, "P" tile, sentence case). Rendered per request and streamed,
/// never stored. Lato ships inside QuestPDF, so output is identical on a developer machine and on the Alpine image.
/// </summary>
static class ReceiptDocument
{
    const string Font = "Lato";
    static readonly Color Brand = Color.FromHex(P.Brand), BrandSoft = Color.FromHex(P.BrandSoft), Ink = Color.FromHex(P.Ink),
        Muted = Color.FromHex(P.Muted), Line = Color.FromHex(P.Line), Well = Color.FromHex(P.Well),
        Ok = Color.FromHex(P.Ok), OkSoft = Color.FromHex(P.OkSoft), Warn = Color.FromHex(P.Warn), WarnSoft = Color.FromHex(P.WarnSoft);

    static readonly Dictionary<string, string> Methods = new() { ["card"] = "Card", ["mobilemoney"] = "Mobile money", ["payatgate"] = "Pay at gate" };
    static readonly Dictionary<string, string> Rules = new()
    {
        ["24h_full"] = "Full refund (24h+ lead)", ["4h_half"] = "Half refund (4h+ lead)",
        ["late_none"] = "Late cancellation", ["policy_payout"] = "Spot Cover payout",
    };

    public static byte[] Render(BookingReceipt r, string appName) => Booking(r, appName).GeneratePdf();

    public static byte[] Render(RefundReceipt r, string appName) => Refund(r, appName).GeneratePdf();

    public static IDocument Booking(BookingReceipt r, string appName) => Document.Create(document => document.Page(page =>
    {
        Defaults(page);
        page.Header().Element(c => Header(c, appName, "Booking receipt", ("Receipt no.", r.Number), ("Booked", Day(r.BookedAt))));
        page.Content().PaddingTop(20).Column(column =>
        {
            column.Spacing(18);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Block(c, "Billed to", r.CustomerName, r.CustomerEmail));
                row.ConstantItem(16);
                row.RelativeItem().Element(c => Block(c, "Parking at", r.LotName, r.LotAddress, r.LotPhone));
            });
            column.Item().Element(c => Booked(c, r));
            column.Item().Element(c => Charges(c, r));
            column.Item().Element(c => PaymentBox(c, r));
            column.Item().PaddingTop(4).Text($"Thank you for parking with {appName}. Keep this receipt for your records.").FontSize(8.5f).FontColor(Muted);
        });
        page.Footer().Element(c => Footer(c, appName));
    }));

    public static IDocument Refund(RefundReceipt r, string appName) => Document.Create(document => document.Page(page =>
    {
        Defaults(page);
        var b = r.Booking;
        page.Header().Element(c => Header(c, appName, "Refund receipt", ("Receipt no.", r.Number), ("Requested", Day(r.RequestedAt))));
        page.Content().PaddingTop(20).Column(column =>
        {
            column.Spacing(18);
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Block(c, "Refunded to", b.CustomerName, b.CustomerEmail));
                row.ConstantItem(16);
                row.RelativeItem().Element(c => Block(c, "Booking", $"{b.LotName}, spot {b.SpotLabel}", b.Number, LotTime.Span(b.StartAtUtc, b.EndAtUtc, b.LotTimeZoneId)));
            });
            column.Item().Background(Well).CornerRadius(8).Padding(14).Column(c =>
            {
                c.Spacing(6);
                c.Item().Row(row =>
                {
                    row.RelativeItem().Text("Refund").FontSize(10).Bold();
                    row.AutoItem().Element(p => Pill(p, Capitalize(r.Status), r.Status == "paid" ? Ok : Warn, r.Status == "paid" ? OkSoft : WarnSoft));
                });
                c.Item().Element(x => Block(x, "Rule", Rules.GetValueOrDefault(r.Rule, r.Rule)));
                c.Item().Element(x => Block(x, "Reason", r.Reason));
            });
            column.Item().AlignRight().Width(240).Column(c =>
            {
                c.Item().Element(x => AmountLine(x, "Booking total", Money(b.TotalCents), Muted));
                c.Item().PaddingTop(6).Background(BrandSoft).CornerRadius(6).PaddingVertical(9).PaddingHorizontal(10).Row(row =>
                {
                    row.RelativeItem().Text("Refund amount").FontSize(11).Bold();
                    row.AutoItem().Text(Money(r.AmountCents)).FontSize(13).Bold().FontColor(Brand);
                });
                if (r.PaidAt is { } paid) c.Item().PaddingTop(4).AlignRight().Text("Paid " + Day(paid)).FontSize(8.5f).FontColor(Muted);
            });
            column.Item().PaddingTop(4).Text("The refund returns to the original payment method. Card and mobile money providers decide when it shows on your statement.")
                .FontSize(8.5f).FontColor(Muted);
        });
        page.Footer().Element(c => Footer(c, appName));
    }));

    static void Booked(IContainer container, BookingReceipt r) => container.Border(0.75f).BorderColor(Line).CornerRadius(8).Padding(12).Column(column =>
    {
        column.Spacing(8);
        column.Item().Text("Your booking").FontSize(10).Bold();
        column.Item().Row(row =>
        {
            row.RelativeItem().Element(c => Block(c, "Spot", r.SpotLabel));
            row.RelativeItem().Element(c => Block(c, "Plate", string.IsNullOrWhiteSpace(r.PlateNo) ? "Not given" : r.PlateNo.ToUpperInvariant()));
            row.RelativeItem().Element(c => Block(c, "Type", r.WindowType == "recurring" ? $"Recurring, {r.OccurrenceCount} windows" : "Single window"));
        });
        column.Item().Element(c => Block(c, r.WindowType == "recurring" ? "First window to last" : "Window", LotTime.Span(r.StartAtUtc, r.EndAtUtc, r.LotTimeZoneId)));
    });

    static void Charges(IContainer container, BookingReceipt r) => container.Column(column =>
    {
        column.Item().Element(c => AmountLine(c, "Parking", Money(r.ParkingCents), Ink));
        if (r.InsuranceCents > 0) column.Item().Element(c => AmountLine(c, "Spot Cover", Money(r.InsuranceCents), Ink));
        column.Item().PaddingTop(6).Background(BrandSoft).CornerRadius(6).PaddingVertical(9).PaddingHorizontal(10).Row(row =>
        {
            row.RelativeItem().Text("Total").FontSize(11).Bold();
            row.AutoItem().Text(Money(r.TotalCents)).FontSize(13).Bold().FontColor(Brand);
        });
    });

    static void PaymentBox(IContainer container, BookingReceipt r) => container.Border(0.75f).BorderColor(Line).CornerRadius(8).Padding(12).Column(column =>
    {
        column.Spacing(8);
        var status = r.PaymentStatus ?? "pending";
        var good = status is "paid" or "refunded";
        column.Item().Row(row =>
        {
            row.RelativeItem().Text("Payment").FontSize(10).Bold();
            row.AutoItem().Element(c => Pill(c, Capitalize(status), good ? Ok : Warn, good ? OkSoft : WarnSoft));
        });
        column.Item().Row(row =>
        {
            row.RelativeItem().Element(c => Block(c, "Method", r.PaymentMethod is { } m ? Methods.GetValueOrDefault(m, m) : "Not paid yet"));
            row.RelativeItem().Element(c => Block(c, "Reference", r.PaymentId is { } id ? ShortId(id).ToUpperInvariant() : "None"));
            row.RelativeItem().Element(c => Block(c, "Paid", r.PaidAt is { } paid ? Day(paid) : "Not paid yet"));
        });
    });

    static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    static void Defaults(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(44);
        page.MarginVertical(40);
        page.DefaultTextStyle(t => t.FontFamily(Font).FontSize(9.5f).FontColor(Ink).LineHeight(1.35f));
    }

    /// <summary>The "P" tile and app name on the left; document title and reference on the right.</summary>
    static void Header(IContainer container, string appName, string title, params (string Label, string Value)[] meta) => container.Column(column =>
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Row(brand =>
            {
                brand.ConstantItem(28).Height(28).Background(Brand).CornerRadius(7).AlignCenter().AlignMiddle()
                    .Text(P.Mark).FontSize(16).Bold().FontColor(Colors.White);
                brand.ConstantItem(10);
                brand.AutoItem().Height(28).AlignMiddle().Text(appName).FontSize(15).Bold();
            });
            row.ConstantItem(230).Column(right =>
            {
                right.Item().AlignRight().Text(title).FontSize(11).Bold().FontColor(Brand);
                foreach (var (label, value) in meta)
                    right.Item().AlignRight().PaddingTop(2).Text(t =>
                    {
                        t.Span(label + "  ").FontColor(Muted);
                        t.Span(value).Bold();
                    });
            });
        });
        column.Item().PaddingTop(16).LineHorizontal(1).LineColor(Line);
    });

    static void Footer(IContainer container, string appName) => container.BorderTop(0.75f).BorderColor(Line).PaddingTop(8).Row(row =>
    {
        row.RelativeItem().Text(appName).FontSize(8).FontColor(Muted);
        row.AutoItem().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
            t.Span("Page ");
            t.CurrentPageNumber();
            t.Span(" of ");
            t.TotalPages();
        });
    });

    /// <summary>A muted label above a block of lines.</summary>
    static void Block(IContainer container, string label, params string?[] lines) => container.Column(column =>
    {
        column.Item().Text(label).FontSize(8).Bold().FontColor(Muted);
        var first = true;
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var text = column.Item().PaddingTop(first ? 3 : 1).Text(line!).FontSize(first ? 10 : 9.5f).FontColor(first ? Ink : Muted);
            if (first) text.Bold();
            first = false;
        }
    });

    static void AmountLine(IContainer container, string label, string value, Color valueColor) =>
        container.PaddingVertical(2.5f).PaddingHorizontal(10).Row(row =>
        {
            row.RelativeItem().Text(label).FontColor(Muted);
            row.AutoItem().Text(value).FontColor(valueColor);
        });

    static void Pill(IContainer container, string text, Color fore, Color back) =>
        container.Background(back).CornerRadius(4).PaddingHorizontal(7).PaddingVertical(2).Text(text).FontSize(8).Bold().FontColor(fore);
}
