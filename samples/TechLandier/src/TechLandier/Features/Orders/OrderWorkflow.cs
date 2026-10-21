namespace TechLandier.Features;

/// Static status-machine guard: pending → paid → processing → shipped → delivered, cancel via != delivered.
/// The transition engine that applies a move (and its side effects) lives in OrderService.
public static class OrderWorkflow
{
    static readonly Dictionary<string, string[]> From = new()
    {
        [OrderStatuses.Pending] = [OrderStatuses.Paid, OrderStatuses.Cancelled],
        [OrderStatuses.Paid] = [OrderStatuses.Processing, OrderStatuses.Cancelled],
        [OrderStatuses.Processing] = [OrderStatuses.Shipped, OrderStatuses.Cancelled],
        [OrderStatuses.Shipped] = [OrderStatuses.Delivered],
        [OrderStatuses.Delivered] = [],
        [OrderStatuses.Cancelled] = [],
    };

    public static bool Can(string status, string to) => From.GetValueOrDefault(status, []).Contains(to);

    public static string CheckError(Order order, string to) =>
        $"Cannot move order {order.Number} from {order.Status} to {to}.";
}
