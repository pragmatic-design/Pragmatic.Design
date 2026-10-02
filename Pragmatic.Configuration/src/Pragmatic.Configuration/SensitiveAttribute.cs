namespace Pragmatic.Configuration;

/// <summary>
///     Marks a property on a <c>[Configuration]</c> options class as sensitive: its value must never be
///     written in plaintext to side channels such as the audit log. The source generator records the key at
///     compile time so a store can mask it (writing <c>"(sensitive)"</c> instead of the value) with zero
///     reflection.
/// </summary>
/// <remarks>
///     This governs <b>audit masking</b>, not encryption at rest. Use a secret store for values that must be
///     encrypted; use <c>[Sensitive]</c> for configuration values whose plaintext should not appear in the
///     change history (e.g. connection fragments, tokens surfaced through configuration).
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SensitiveAttribute : Attribute;
