namespace CarParker.Infrastructure;

/// <summary>
/// Identifies the process that served a request. Compose does not give each replicated container a
/// distinct environment value, so the container hostname is the default; <c>INSTANCE_ID</c> overrides it.
/// </summary>
public static class ReplicaIdentity
{
    public const string HeaderName = "X-App-Replica";

    public static string FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration["INSTANCE_ID"];
        var value = string.IsNullOrWhiteSpace(configured) ? Environment.MachineName : configured.Trim();
        var safe = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return safe.Length == 0 ? "unknown" : safe;
    }
}

public sealed class ReplicaHeaderMiddleware(RequestDelegate next, IConfiguration configuration)
{
    readonly string instanceId = ReplicaIdentity.FromConfiguration(configuration);

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers[ReplicaIdentity.HeaderName] = instanceId;
        await next(context);
    }
}
