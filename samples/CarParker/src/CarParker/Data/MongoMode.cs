namespace CarParker.Data;

/// How the app talks to MongoDB. <c>Single</c> (default) runs the same write paths without multi-document
/// transactions; <c>Replica</c> requires a replica set (or sharded cluster) and uses transactions.
public enum MongoMode
{
    Single,
    Replica,
}

public static class MongoModeConfig
{
    public static MongoMode Read(IConfiguration configuration) => Parse(configuration["MONGO_MODE"]);

    public static MongoMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "single" => MongoMode.Single,
        "replica" or "replica-set" or "replicaset" => MongoMode.Replica,
        _ => throw new InvalidOperationException($"MONGO_MODE must be 'single' or 'replica' (got '{value}')."),
    };
}
