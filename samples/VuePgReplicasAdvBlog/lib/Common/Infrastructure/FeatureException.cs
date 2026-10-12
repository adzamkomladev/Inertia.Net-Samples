namespace VuePgReplicasAdvBlog.Infrastructure;

public enum FailureKind { InvalidInput, Validation, Missing, AccessDenied, Expired }

/// A feature failure; applications decide how to present it to their clients.
public sealed class FeatureException(FailureKind kind, string? message = null, Dictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public FailureKind Kind { get; } = kind;
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];

    public static FeatureException Invalid(string field, string message) =>
        new(FailureKind.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
