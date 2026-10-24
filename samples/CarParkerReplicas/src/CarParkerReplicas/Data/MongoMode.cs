namespace CarParker.Data;

public enum MongoMode { Single, Replica }

public static class MongoModeConfig
{
    public static MongoMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "single" => MongoMode.Single,
        "replica" or "replica-set" or "replicaset" => MongoMode.Replica,
        _ => throw new InvalidOperationException("MONGO_MODE must be 'single' or 'replica'."),
    };
}
