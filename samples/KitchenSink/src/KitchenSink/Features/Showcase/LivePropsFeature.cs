using System.Threading.Channels;
using KitchenSink.Data;
using KitchenSink.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Showcase;

public static class LivePropsFeature
{
    static readonly string[] Frameworks = ["vue", "react", "svelte"];
    static readonly string[] Reactions = ["thumbsUp", "party", "heart", "rocket"];

    public static IEndpointRouteBuilder MapLiveProps(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/data-loading/live-props").RequireAuthorization();

        group.MapGet("", Show);
        group.MapPost("/votes", CastVote);
        group.MapPost("/reactions", AddReaction);
        group.MapPost("/ticks", BurstTicks);

        return endpoints;
    }

    static async Task<IResult> Show(HttpContext context, AppDb db, CancellationToken cancellationToken)
    {
        var visitor = Visitor(context);

        return Inertia.Net.Inertia.Render("Features/DataLoading/LiveProps", new InertiaProps
        {
            ["visitor"] = visitor,
            ["votes"] = Inertia.Net.Inertia.Prop(async ct => await VotesAsync(db, ct)).Live(DemoEvents.VoteCast),
            // Resolved for each visitor, which is why it refreshes rather than travelling with the event.
            ["yourVote"] = Inertia.Net.Inertia.Prop(async ct =>
                    await db.LiveVotes.Where(v => v.Visitor == visitor).Select(v => v.Framework).FirstOrDefaultAsync(ct))
                .Live(DemoEvents.VoteCast),
            ["reactions"] = Inertia.Net.Inertia.Prop(async ct => await ReactionsAsync(db, ct)).Live(DemoEvents.ReactionAdded),
            ["ticks"] = Inertia.Net.Inertia.Prop(async ct => await db.LiveTicks.CountAsync(ct)).Live(DemoEvents.CounterTicked),
            ["slowTicks"] = Inertia.Net.Inertia.Prop(async ct => await db.LiveTicks.CountAsync(ct))
                .Live(DemoEvents.CounterTicked)
                .Throttle(TimeSpan.FromSeconds(3)),
        });
    }

    static async Task<IResult> CastVote(
        HttpContext context,
        AppDb db,
        IInertiaLive live,
        CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var framework = input.String("framework");
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (framework is null || !Frameworks.Contains(framework))
        {
            errors["framework"] = ["The selected framework is invalid."];
        }

        if (errors.Count > 0)
        {
            return ValidationFailure(context, errors);
        }

        var visitor = Visitor(context);
        var vote = await db.LiveVotes.FirstOrDefaultAsync(v => v.Visitor == visitor, cancellationToken);

        if (vote is null)
        {
            vote = new LiveVote { Visitor = visitor, Framework = framework! };
            db.LiveVotes.Add(vote);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (vote.Framework == framework)
        {
            return Results.NoContent();
        }
        else
        {
            vote.Framework = framework!;
            await db.SaveChangesAsync(cancellationToken);
        }

        var except = input.Bool("toOthers") ? context.InertiaRequest().SocketId : null;
        await live.PublishAsync(DemoEvents.VoteCast, new { visitor, framework }, except, cancellationToken);
        return Results.NoContent();
    }

    static async Task<IResult> AddReaction(
        HttpContext context,
        AppDb db,
        IInertiaLive live,
        CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var reaction = input.String("reaction");
        if (reaction is null || !Reactions.Contains(reaction))
        {
            return ValidationFailure(context, new Dictionary<string, string[]>
            {
                ["reaction"] = ["The selected reaction is invalid."],
            });
        }

        var visitor = Visitor(context);
        db.LiveReactions.Add(new LiveReaction { Visitor = visitor, Reaction = reaction });
        await db.SaveChangesAsync(cancellationToken);

        await live.PublishAsync(
            DemoEvents.ReactionAdded,
            new { reactions = await ReactionsAsync(db, cancellationToken) },
            cancellationToken: cancellationToken);

        return Results.NoContent();
    }

    static IResult BurstTicks(LiveTickBurstService bursts)
    {
        bursts.Enqueue(20);
        return Results.NoContent();
    }

    static IResult ValidationFailure(HttpContext context, Dictionary<string, string[]> errors)
    {
        if (context.Request.Headers.ContainsKey("X-Inertia")
            || context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return InertiaForms.BackWithErrors(context, errors);
        }

        return Results.ValidationProblem(errors);
    }

    static string Visitor(HttpContext context)
    {
        const string key = "live-props.visitor";
        var visitor = context.Session.GetString(key);
        if (!string.IsNullOrWhiteSpace(visitor))
        {
            return visitor;
        }

        visitor = "Visitor " + new string(Enumerable.Range(0, 6)
            .Select(_ => (char)Random.Shared.Next('A', 'Z' + 1))
            .ToArray());
        context.Session.SetString(key, visitor);
        return visitor;
    }

    static async Task<Dictionary<string, int>> VotesAsync(AppDb db, CancellationToken cancellationToken)
    {
        var counts = await db.LiveVotes.AsNoTracking()
            .GroupBy(v => v.Framework)
            .Select(g => new { Framework = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Framework, x => x.Count, cancellationToken);

        return Frameworks.ToDictionary(f => f, f => counts.GetValueOrDefault(f));
    }

    static async Task<Dictionary<string, int>> ReactionsAsync(AppDb db, CancellationToken cancellationToken)
    {
        var counts = await db.LiveReactions.AsNoTracking()
            .GroupBy(r => r.Reaction)
            .Select(g => new { Reaction = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Reaction, x => x.Count, cancellationToken);

        return Reactions.ToDictionary(r => r, r => counts.GetValueOrDefault(r));
    }
}

/// <summary>Sends the 20-event burst after the response has been returned, one tick every 200ms.</summary>
public sealed class LiveTickBurstService(
    IServiceScopeFactory scopes,
    IInertiaLive live,
    ILogger<LiveTickBurstService> logger) : BackgroundService
{
    readonly Channel<int> _bursts = Channel.CreateUnbounded<int>();

    public void Enqueue(int count) => _bursts.Writer.TryWrite(count);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var count in _bursts.Reader.ReadAllAsync(stoppingToken))
            {
                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                        db.LiveTicks.Add(new LiveTick { CreatedAt = DateTime.UtcNow });
                        await db.SaveChangesAsync(stoppingToken);
                        await live.PublishAsync(DemoEvents.CounterTicked, cancellationToken: stoppingToken);
                        await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "A live tick could not be broadcast.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
