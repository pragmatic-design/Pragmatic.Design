namespace Pragmatic.Identity.Auditing;

/// <summary>
///     The locator used when an application registers none: the identity the event carried, under the
///     subject type <see cref="SubjectType" />.
/// </summary>
/// <remarks>
///     <para>
///         A named default: an application whose registry really is keyed by
///         <c>("User", &lt;login e-mail&gt;)</c> is correctly served by it without registering a locator
///         of its own.
///     </para>
///     <para>
///         ⚠️ <b>It is a default, not a contract.</b> For any application that registers its subjects
///         under its own type — which the framework's own <c>[DataSubject]</c> shape encourages — this
///         resolves nothing, every entry is written with no subject, and per-subject correlation
///         silently finds nothing. That application implements <see cref="ISecuritySubjectLocator" />
///         and passes it to <c>AddIdentitySecurityAuditing</c>; the overload exists so the choice is
///         made at the call site rather than discovered from an empty report.
///     </para>
/// </remarks>
public sealed class LoginIdentityIsTheSubjectKey : ISecuritySubjectLocator
{
    /// <summary>The subject type this default looks under.</summary>
    public const string SubjectType = "User";

    /// <inheritdoc />
    public ValueTask<SecuritySubjectKey?> LocateAsync(
        string identity, LoginIdentityKind kind, CancellationToken ct = default)
        => new(string.IsNullOrWhiteSpace(identity)
            ? null
            : new SecuritySubjectKey(SubjectType, identity));
}
