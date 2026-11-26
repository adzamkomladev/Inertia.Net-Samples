using System.Security.Claims;

namespace KitchenSink.Features;

public static class DemoEvents
{
    public static LiveEvent Reset { get; } = new("DemoDataReset", LiveChannel.Public("demo"));
    public static LiveEvent VoteCast { get; } = new("VoteCast", LiveChannel.Public("demo"));
    public static LiveEvent ReactionAdded { get; } = new("ReactionAdded", LiveChannel.Public("demo"));
    public static LiveEvent CounterTicked { get; } = new("CounterTicked", LiveChannel.Public("demo"));
}

public static class ClaimsPrincipalExtensions
{
    public static int? UserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
