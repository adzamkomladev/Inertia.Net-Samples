using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static TechLandier.Features.DocumentStyle;

namespace TechLandier.Features;

/// The BNPL plan statement PDF. Waived installments are shown but are neither owed nor counted as paid.
static class StatementDocument
{
    public static byte[] Render(StatementData s, DateTime nowUtc) => RenderDocument(s, nowUtc).GeneratePdf();

    public static IDocument RenderDocument(StatementData s, DateTime nowUtc) => Document.Create(document =>
    {
        document.Page(page =>
        {
            Defaults(page);
            page.Header().Element(c => Header(c, s.Store, "Installment statement",
                ("Order", s.OrderNumber), ("Statement date", Date(nowUtc, s.Store)), ("Plan started", Date(s.StartedAt, s.Store))));
            page.Content().PaddingTop(20).Column(column =>
            {
                column.Spacing(18);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Block(c, "Account holder", s.CustomerName, s.CustomerEmail));
                    row.ConstantItem(16);
                    row.RelativeItem().Element(c => Block(c, "Plan", $"{s.Count} installments of {Pricing.Show(s.PerInstallment)}",
                        $"Every 14 days · plan fee {Pricing.Show(s.Fee)} included", "Status: " + Capitalize(s.PlanStatus)));
                });
                column.Item().Element(c => Summary(c, s));
                if (s.Next is { } next) column.Item().Element(c => NextPayment(c, next));
                column.Item().Element(c => Schedule(c, s));
                column.Item().Text("Waived installments are not owed and are left out of the totals. Paying an installment early clears the oldest unpaid one first.")
                    .FontSize(8.5f).FontColor(Muted);
            });
            page.Footer().Element(c => Footer(c, s.Store));
        });
    });

    static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    static void Summary(IContainer container, StatementData s) => container.Row(row =>
    {
        void Card(string label, string value, Color? tone = null, bool last = false)
        {
            row.RelativeItem().Background(Soft).CornerRadius(8).Padding(10).Column(c =>
            {
                c.Item().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted).LetterSpacing(0.08f);
                c.Item().PaddingTop(3).Text(value).FontSize(14).Bold().FontColor(tone ?? Ink);
            });
            if (!last) row.ConstantItem(8);
        }
        Card("Plan total", Pricing.Show(s.PlanTotal));
        Card("Paid", Pricing.Show(s.Paid), Success);
        Card("Remaining", Pricing.Show(s.Remaining), s.Overdue > 0 ? Danger : Ink, last: true);
    });

    static void NextPayment(IContainer container, StatementRow next)
    {
        var overdue = next.Status == InstallmentStatuses.Overdue;
        container.Background(overdue ? DangerSoft : BrandSoft).CornerRadius(8).Padding(12).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(overdue ? "Overdue payment" : "Next payment").Bold().FontColor(overdue ? Danger : Brand);
                c.Item().Text($"Installment {next.Number}, due {DateOnlyText(next.DueDate)}").FontColor(Muted);
            });
            row.AutoItem().AlignMiddle().Text(Pricing.Show(next.Amount)).FontSize(15).Bold().FontColor(overdue ? Danger : Brand);
        });
    }

    static void Schedule(IContainer container, StatementData s) => container.Table(table =>
    {
        table.ColumnsDefinition(c =>
        {
            c.ConstantColumn(34);
            c.RelativeColumn(3);
            c.RelativeColumn(2);
            c.RelativeColumn(2);
            c.RelativeColumn(3);
        });
        table.Header(h =>
        {
            h.Cell().Element(HeadCell).Text("#");
            h.Cell().Element(HeadCell).Text("Due date");
            h.Cell().Element(HeadCell).AlignRight().Text("Amount");
            h.Cell().Element(HeadCell).PaddingLeft(14).Text("Status");
            h.Cell().Element(HeadCell).Text("Paid on");
        });
        foreach (var row in s.Rows)
        {
            var (fore, back) = StatusColors(row.Status);
            table.Cell().Element(BodyCell).Text(row.Number.ToString()).Bold();
            table.Cell().Element(BodyCell).Text(DateOnlyText(row.DueDate));
            table.Cell().Element(BodyCell).AlignRight().Text(Pricing.Show(row.Amount)).Bold().Strikethrough(row.Status == InstallmentStatuses.Waived);
            table.Cell().Element(BodyCell).PaddingLeft(14).AlignLeft().Element(c => c.AlignLeft().Element(x => Pill(x, row.Status, fore, back)));
            table.Cell().Element(BodyCell).Text(row.PaidAt is { } at ? Date(at, s.Store) : "—").FontColor(row.PaidAt is null ? Muted : Ink);
        }
    });
}
