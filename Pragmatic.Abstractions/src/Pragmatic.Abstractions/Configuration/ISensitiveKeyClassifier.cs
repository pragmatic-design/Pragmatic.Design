namespace Pragmatic.Configuration;

/// <summary>
///     Classifies configuration keys as sensitive so that stores never persist their plaintext value into
///     side channels such as the audit log. Populated at compile time by the source generator from
///     <c>[Sensitive]</c>-annotated properties on <c>[Configuration]</c> options classes — zero reflection.
/// </summary>
/// <remarks>
///     A key is matched by its logical form, ignoring any environment-overlay prefix
///     (e.g. both <c>"Booking:ApiKey"</c> and <c>"staging/Booking:ApiKey"</c> resolve to the same
///     sensitive key). The default implementation (<see cref="NullSensitiveKeyClassifier" />) treats no key
///     as sensitive; the generator replaces it when at least one <c>[Sensitive]</c> property exists.
/// </remarks>
public interface ISensitiveKeyClassifier
{
    /// <summary>Returns <c>true</c> when the value of <paramref name="key" /> must be masked in audit trails.</summary>
    bool IsSensitive(string key);
}
