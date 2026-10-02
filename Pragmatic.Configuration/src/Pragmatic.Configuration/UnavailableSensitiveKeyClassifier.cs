namespace Pragmatic.Configuration;

/// <summary>
///     Stands in for <see cref="ISensitiveKeyClassifier" /> when the generated aggregator never ran,
///     and treats <b>every</b> key as sensitive.
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>Add{Prefix}Configuration</c> always registers a classifier: the
///         compile-time set when an assembly declares <c>[Sensitive]</c> properties, and
///         <see cref="NullSensitiveKeyClassifier" /> when it declares none. So reaching this type
///         means the aggregator is absent — the generator did not run, or its output was discarded —
///         and nothing knows which keys are secret.
///     </para>
///     <para>
///         Answering <see langword="false" /> there is not neutral. Two consumers rely on this to
///         keep a secret in: <c>SensitiveWriteGuardConfigurationStore</c> refuses to store a
///         plaintext value where a <c>SecretReference</c> belongs, and
///         <c>DatabaseConfigurationStore</c> masks the value it writes to the audit trail. Both
///         become no-ops when every key is "not sensitive", so the failure mode is a secret in
///         cleartext in an append-only trail. Over-masking is the recoverable direction; this class
///         picks it deliberately.
///     </para>
/// </remarks>
public sealed class UnavailableSensitiveKeyClassifier : ISensitiveKeyClassifier
{
    /// <summary>The shared instance.</summary>
    public static readonly UnavailableSensitiveKeyClassifier Instance = new();

    /// <inheritdoc />
    public bool IsSensitive(string key) => true;
}
