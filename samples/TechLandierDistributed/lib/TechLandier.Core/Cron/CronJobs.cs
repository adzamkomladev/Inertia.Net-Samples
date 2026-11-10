using Cronos;

namespace TechLandier.Infrastructure;

public delegate Task CronTask(CronTasks tasks, CancellationToken ct);

/// The schedule catalog. Runwick's scheduler materializes due occurrences as durable jobs; the Worker's
/// `cron-fixed` pool runs them one at a time. Every task is safe to run twice.
public static class CronJobs
{
    public static readonly (string Name, string Cron, CronTask Run)[] Schedule =
    [
        ("sales-rollup", "*/15 * * * *", async (t, ct) => await t.SalesRollupAsync(ct)),
        ("bnpl-reminders", "0 7 * * *", async (t, ct) => await t.BnplRemindersAsync(ct)),
        ("bnpl-overdue", "10 7 * * *", async (t, ct) => await t.BnplOverdueAsync(ct)),
        ("bnpl-statement", "0 8 * * 1", async (t, ct) => await t.BnplStatementAsync(ct)),
        ("ticket-escalation", "0 * * * *", async (t, ct) => await t.TicketEscalationAsync(ct)),
        ("order-timeout", "0 * * * *", async (t, ct) => await t.OrderTimeoutAsync(ct)),
        ("prune-product-views", "0 3 * * *", async (t, ct) => await t.PruneViewsAsync(ct)),
        ("orphan-assets", "0 * * * *", async (t, ct) => await t.OrphanAssetsAsync(ct)),
        ("prune-unverified", "0 4 * * *", async (t, ct) => await t.PruneUnverifiedAsync(ct)),
        ("prune-jobs", "30 3 * * *", async (t, ct) => await t.PruneJobsAsync(ct)),
    ];

    /// <summary>`Cron:<name>` (env CRON_&lt;NAME&gt;) overrides the default expression. Parsed eagerly so a typo fails startup.</summary>
    public static string Expression(IConfiguration config, string name)
    {
        var entry = Schedule.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        var text = config[$"Cron:{name}"] is { Length: > 0 } value ? value.Trim() : entry.Cron;
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (fields is not (5 or 6))
            throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{name}: expected five or six fields.");
        try
        {
            _ = CronExpression.Parse(text, fields == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
        }
        catch (CronFormatException e)
        {
            throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{name}: {e.Message}", e);
        }
        return text;
    }

    public static Runwick.ScheduleCronFormat Format(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? Runwick.ScheduleCronFormat.SixField
            : Runwick.ScheduleCronFormat.FiveField;

    public static bool TryGet(string name, out (string Name, string Cron, CronTask Run) entry)
    {
        entry = Schedule.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        return entry.Run is not null;
    }
}
