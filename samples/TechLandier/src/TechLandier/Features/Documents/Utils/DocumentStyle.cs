using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TechLandier.Features;

/// Shared look of the receipt and statement PDFs, matching the emails: product-blue accent on quiet neutral surfaces.
/// Lato ships inside QuestPDF, so output is identical on a developer machine and on the Alpine image.
static class DocumentStyle
{
    public const string Font = "Lato";
    public static readonly Color Brand = Color.FromHex("#1957d2");
    public static readonly Color BrandSoft = Color.FromHex("#e4ecf9");
    public static readonly Color Ink = Color.FromHex("#12161b");
    public static readonly Color Muted = Color.FromHex("#595e64");
    public static readonly Color Line = Color.FromHex("#e1e3e5");
    public static readonly Color Soft = Color.FromHex("#f4f6f8");
    public static readonly Color Success = Color.FromHex("#0b7643");
    public static readonly Color SuccessSoft = Color.FromHex("#e3f3ea");
    public static readonly Color Warning = Color.FromHex("#975800");
    public static readonly Color Danger = Color.FromHex("#c2181d");
    public static readonly Color DangerSoft = Color.FromHex("#fbe6e6");

    public static string Date(DateTime utc, StoreInfo store) => store.Local(utc).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    public static string DateOnlyText(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    public static string Negative(decimal amount) => "−" + Pricing.Show(amount);

    public static void Defaults(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(44);
        page.MarginVertical(40);
        page.DefaultTextStyle(t => t.FontFamily(Font).FontSize(9.5f).FontColor(Ink).LineHeight(1.35f));
    }

    /// Brand mark, store name left; document title and reference right.
    public static void Header(IContainer container, StoreInfo store, string title, params (string Label, string Value)[] meta)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Row(brand =>
                {
                    brand.ConstantItem(28).Height(28).Background(Brand).CornerRadius(7).AlignCenter().AlignMiddle()
                        .Text("T").FontSize(16).Bold().FontColor(Colors.White);
                    brand.ConstantItem(10);
                    brand.AutoItem().Height(28).AlignMiddle().Text(store.Name).FontSize(15).Bold();
                });
                row.ConstantItem(230).Column(right =>
                {
                    right.Item().AlignRight().Text(title.ToUpperInvariant()).FontSize(10).Bold().FontColor(Brand).LetterSpacing(0.12f);
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
    }

    public static void Footer(IContainer container, StoreInfo store)
    {
        container.BorderTop(0.75f).BorderColor(Line).PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span(store.Name);
                foreach (var part in new[] { store.Email, store.Phone }.Where(p => p.Length > 0)) t.Span("  ·  " + part);
            });
            row.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    }

    /// Small caps label above a block of lines.
    public static void Block(IContainer container, string label, params string?[] lines)
    {
        container.Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted).LetterSpacing(0.1f);
            var first = true;
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var text = column.Item().PaddingTop(first ? 4 : 1).Text(line!).FontSize(first ? 10 : 9.5f).FontColor(first ? Ink : Muted);
                if (first) text.Bold();
                first = false;
            }
        });
    }

    public static IContainer HeadCell(IContainer c) =>
        c.Background(Soft).PaddingVertical(6).PaddingHorizontal(8).DefaultTextStyle(t => t.FontSize(8).Bold().FontColor(Muted));

    public static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.75f).BorderColor(Line).PaddingVertical(7).PaddingHorizontal(8);

    public static void Pill(IContainer container, string text, Color fore, Color back) =>
        container.Background(back).CornerRadius(4).PaddingHorizontal(7).PaddingVertical(2)
            .Text(text.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(fore).LetterSpacing(0.06f);

    public static (Color Fore, Color Back) StatusColors(string status) => status switch
    {
        InstallmentStatuses.Paid => (Success, SuccessSoft),
        InstallmentStatuses.Overdue => (Danger, DangerSoft),
        InstallmentStatuses.Waived => (Muted, Soft),
        _ => (Brand, BrandSoft),
    };
}
