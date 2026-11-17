using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

/// The store identity printed on receipts, statements and email footers (from the admin's store settings).
public sealed record StoreInfo(string Name, string Email, string Phone, string Address, string TimeZoneId)
{
    public static readonly StoreInfo Sample = new("TechLandier", "support@techlandier.example", "+233 24 000 0000", "12 Oxford Street, Osu, Accra", "Africa/Accra");

    public static async Task<StoreInfo> LoadAsync(AppDb db, CancellationToken ct)
    {
        var s = await db.Set<StoreSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new StoreSettings();
        return new(s.Name, s.Email, s.Phone, s.Address, s.TimeZoneId);
    }

    /// A UTC timestamp in the store's own zone; an unknown zone id falls back to UTC.
    public DateTime Local(DateTime utc)
    {
        try
        {
            var id = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneId, out var iana) ? iana : TimeZoneId;
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(id));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc;
        }
    }
}

public sealed record ReceiptLine(string Name, string Sku, int Qty, decimal UnitPrice, decimal Total);

/// A BNPL plan as it reads on a receipt: the order is covered by the schedule, plus the plan fee.
public sealed record ReceiptPlan(int Count, decimal PerInstallment, decimal Fee, DateOnly FirstDue);

/// Everything a receipt needs, shared by the PDF and the order-paid email.
public sealed record ReceiptData(
    StoreInfo Store, string OrderNumber, string ReceiptNumber, DateTime PlacedAt, DateTime PaidAt,
    string CustomerName, string CustomerEmail, string Recipient, string Phone, string Street,
    List<ReceiptLine> Lines, decimal Subtotal, decimal Discount, decimal Shipping, decimal Total,
    string PaymentMethod, string? PaymentRef, ReceiptPlan? Plan)
{
    public static string MethodLabel(string method) => method switch
    {
        PaymentMethods.Card => "Card",
        PaymentMethods.MobileMoney => "Mobile money",
        PaymentMethods.PayOnDelivery => "Pay on delivery",
        _ => method.Replace('_', ' '),
    };
}

public sealed record StatementRow(int Number, decimal Amount, DateOnly DueDate, string Status, DateTime? PaidAt);

/// One BNPL plan, for the statement PDF and the weekly statement email.
public sealed record StatementData(
    StoreInfo Store, Guid PlanId, string OrderNumber, string CustomerName, string CustomerEmail, string PlanStatus,
    DateTime StartedAt, DateTime? CompletedAt, int Count, decimal PerInstallment, decimal Fee, List<StatementRow> Rows)
{
    /// Waived installments are not owed and are not counted as paid.
    public decimal PlanTotal => Rows.Where(r => r.Status != InstallmentStatuses.Waived).Sum(r => r.Amount);
    public decimal Paid => Rows.Where(r => r.Status == InstallmentStatuses.Paid).Sum(r => r.Amount);
    public decimal Waived => Rows.Where(r => r.Status == InstallmentStatuses.Waived).Sum(r => r.Amount);
    public decimal Remaining => Rows.Where(r => r.Status is InstallmentStatuses.Due or InstallmentStatuses.Overdue).Sum(r => r.Amount);
    public decimal Overdue => Rows.Where(r => r.Status == InstallmentStatuses.Overdue).Sum(r => r.Amount);
    public StatementRow? Next => Rows.Where(r => r.Status is InstallmentStatuses.Due or InstallmentStatuses.Overdue).OrderBy(r => r.DueDate).FirstOrDefault();
}
