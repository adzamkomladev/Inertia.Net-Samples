using Cronos;
using Inkwell.Infrastructure;
using Runwick;

namespace Inkwell.Cron;

public sealed record CronTask(string Name, string Cron, Func<IServiceProvider, CancellationToken, Task> Run);

/// Registers every entry in <see cref="CronTasks.All"/> as a Runwick schedule. Runwick materializes
/// due occurrences into durable `cron.scheduled` jobs; the `cron-fixed` pool runs them one at a time.
public static class CronSetup
{
    public static RunwickBuilder AddCronSchedules(this RunwickBuilder runwick, IConfiguration config)
    {
        foreach (var task in CronTasks.All)
        {
            var expression = Expression(config, task.Name, task.Cron);
            runwick.AddSchedule(task.Name, new ScheduledCronJob(task.Name))
                .Cron(expression, Format(expression))
                .OnMisfire(MisfirePolicy.RunOnce, TimeSpan.FromMinutes(10))
                .Overlap(ScheduleOverlap.Serialize)
                .WithOptions(new DispatchOptions { Queue = "cron", Priority = "low" });
        }
        return runwick;
    }

    public static string Expression(IConfiguration config, string name, string fallback)
    {
        var text = config[$"Cron:{name}"] is { Length: > 0 } value ? value.Trim() : fallback;
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (fields is not (5 or 6))
            throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{name}: expected five or six fields.");
        try
        {
            _ = CronExpression.Parse(text, fields == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
        }
        catch (CronFormatException e)
        {
            throw new InvalidOperationException($"Invalid cron expression '{text}' for Cron:{name}.", e);
        }
        return text;
    }

    public static ScheduleCronFormat Format(string expression) =>
        expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? ScheduleCronFormat.SixField
            : ScheduleCronFormat.FiveField;
}
