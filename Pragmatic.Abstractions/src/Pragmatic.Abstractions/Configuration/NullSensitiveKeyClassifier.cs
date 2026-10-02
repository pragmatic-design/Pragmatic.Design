namespace Pragmatic.Configuration;

/// <summary>
///     Default <see cref="ISensitiveKeyClassifier" /> used when no <c>[Sensitive]</c> configuration property
///     exists: no key is treated as sensitive. The source generator supersedes this registration whenever an
///     assembly declares at least one sensitive property.
/// </summary>
public sealed class NullSensitiveKeyClassifier : ISensitiveKeyClassifier
{
    /// <summary>Shared instance — the classifier is stateless.</summary>
    public static readonly NullSensitiveKeyClassifier Instance = new();

    /// <inheritdoc />
    public bool IsSensitive(string key) => false;
}
