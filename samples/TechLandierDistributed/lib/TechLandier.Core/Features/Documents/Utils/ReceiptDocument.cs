using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static TechLandier.Features.DocumentStyle;

namespace TechLandier.Features;

/// The order receipt PDF. Real text in an embedded font, so it stays searchable; the item table header repeats across pages.
public static class ReceiptDocument
{
    public static byte[] Render(ReceiptData r) => RenderDocument(r).GeneratePdf();

    public static IDocument RenderDocument(ReceiptData r) => Document.Create(document =>
    {
        document.Page(page =>
        {
            Defaults(page);
            page.Header().Element(c => Header(c, r.Store, "Receipt",
                ("Receipt no.", r.ReceiptNumber), ("Order", r.OrderNumber), ("Paid", Date(r.PaidAt, r.Store))));
            page.Content().PaddingTop(20).Column(column =>
            {
                column.Spacing(18);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Block(c, "Billed to", r.CustomerName, r.CustomerEmail));
                    row.ConstantItem(16);
                    row.RelativeItem().Element(c => Block(c, "Deliver to", r.Recipient, r.Phone, r.Street));
                    row.ConstantItem(16);
                    row.RelativeItem().Element(c => Block(c, "From", r.Store.Name, r.Store.Address, r.Store.Phone));
                });
                column.Item().Element(c => Items(c, r));
                column.Item().Element(c => Totals(c, r));
                column.Item().Element(c => Payment(c, r));
                column.Item().PaddingTop(4).Text("Thank you for shopping with " + r.Store.Name + ". Keep this receipt for warranty and returns.")
                    .FontSize(8.5f).FontColor(Muted);
            });
            page.Footer().Element(c => Footer(c, r.Store));
        });
    });

    static void Items(IContainer container, ReceiptData r) => container.Table(table =>
    {
        table.ColumnsDefinition(c =>
        {
            c.RelativeColumn(5);
            c.ConstantColumn(40);
            c.RelativeColumn(2);
            c.RelativeColumn(2);
        });
        table.Header(h =>
        {
            h.Cell().Element(HeadCell).Text("Item");
            h.Cell().Element(HeadCell).AlignRight().Text("Qty");
            h.Cell().Element(HeadCell).AlignRight().Text("Unit price");
            h.Cell().Element(HeadCell).AlignRight().Text("Amount");
        });
        foreach (var line in r.Lines)
        {
            table.Cell().Element(BodyCell).Column(c =>
            {
                c.Item().Text(line.Name).Bold();
                c.Item().Text("SKU " + line.Sku).FontSize(8).FontColor(Muted);
            });
            table.Cell().Element(BodyCell).AlignRight().Text(line.Qty.ToString());
            table.Cell().Element(BodyCell).AlignRight().Text(Pricing.Show(line.UnitPrice));
            table.Cell().Element(BodyCell).AlignRight().Text(Pricing.Show(line.Total)).Bold();
        }
    });

    static void Totals(IContainer container, ReceiptData r) => container.AlignRight().Width(230).Column(column =>
    {
        column.Item().Element(c => AmountLine(c, "Subtotal", Pricing.Show(r.Subtotal)));
        if (r.Discount > 0) column.Item().Element(c => AmountLine(c, "Discount", Negative(r.Discount), Success));
        column.Item().Element(c => AmountLine(c, "Shipping", r.Shipping > 0 ? Pricing.Show(r.Shipping) : "Free"));
        column.Item().PaddingTop(6).Background(BrandSoft).CornerRadius(6).PaddingVertical(9).PaddingHorizontal(10).Row(row =>
        {
            row.RelativeItem().Text("Total").FontSize(11).Bold();
            row.AutoItem().Text(Pricing.Show(r.Total)).FontSize(13).Bold().FontColor(Brand);
        });
    });

    static void AmountLine(IContainer container, string label, string value, QuestPDF.Infrastructure.Color? color = null) =>
        container.PaddingVertical(2.5f).PaddingHorizontal(10).Row(row =>
        {
            row.RelativeItem().Text(label).FontColor(Muted);
            row.AutoItem().Text(value).FontColor(color ?? Ink);
        });

    static void Payment(IContainer container, ReceiptData r) => container.Border(0.75f).BorderColor(Line).CornerRadius(8).Padding(12).Column(column =>
    {
        column.Spacing(8);
        column.Item().Row(row =>
        {
            row.RelativeItem().Text("Payment").FontSize(10).Bold();
            row.AutoItem().Element(c => Pill(c, "Paid", Success, SuccessSoft));
        });
        column.Item().Row(row =>
        {
            row.RelativeItem().Element(c => Block(c, "Method", ReceiptData.MethodLabel(r.PaymentMethod)));
            row.RelativeItem().Element(c => Block(c, "Reference", r.PaymentRef ?? "—"));
            row.RelativeItem().Element(c => Block(c, "Placed", Date(r.PlacedAt, r.Store)));
        });
        if (r.Plan is { } plan)
            column.Item().Background(Soft).CornerRadius(6).Padding(10).Column(p =>
            {
                p.Item().Text("Installment plan").Bold();
                p.Item().PaddingTop(2).Text(
                    $"{plan.Count} installments of {Pricing.Show(plan.PerInstallment)}, 14 days apart, first due {DateOnlyText(plan.FirstDue)}. "
                    + $"The plan covers the order total of {Pricing.Show(r.Total)} plus a {Pricing.Show(plan.Fee)} plan fee. "
                    + "Each installment is confirmed by its own payment notice and appears on your plan statement.").FontColor(Muted);
            });
    });
}
