namespace Pragmatic.Identity.Auditing;

/// <summary>
///     Which identity a sign-in attempt named, and in what form the event carried it.
/// </summary>
/// <remarks>
///     The two audited events do not name a person the same way, and a locator that had to guess which
///     shape it was given would be an implicit contract — the kind this package already paid for once.
/// </remarks>
public enum LoginIdentityKind
{
    /// <summary>What was typed into the sign-in form: for the local provider, the e-mail address.</summary>
    LoginName = 0,

    /// <summary>
    ///     The composed <c>{issuer}|{subject}</c> key of the identity record, as
    ///     <see cref="Pragmatic.Identity.ExternalIdentityKey" /> builds it.
    /// </summary>
    ExternalIdentityKey = 1,
}

/// <summary>
///     The subject an application's registry holds, as a type and an identifier.
/// </summary>
/// <param name="SubjectType">
///     The type the application registered the subject under — <c>nameof(Employee)</c>, <c>"User"</c>,
///     whatever <see cref="Pragmatic.Privacy.ISubjectRegistry" /> was called with.
/// </param>
/// <param name="Identifier">The identifier under that type, never the reference itself.</param>
public readonly record struct SecuritySubjectKey(string SubjectType, string Identifier);

/// <summary>
///     Turns the identity a security event named into the subject key the application's registry
///     holds — so a failed sign-in can be attributed to the account it was against.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why this exists.</b> A fixed pair such as <c>("User", &lt;login e-mail&gt;)</c> can never
///         match an application keyed by anything else. The framework's own shape produces the mismatch:
///         <c>[DataSubject(nameof(EmployeeNumber))]</c> is how a subject is declared, and an address must
///         not be the reference because an audit trail is append-only and no erasure could reach an
///         e-mail written into it. With a fixed pair every security entry would carry no subject,
///         silently, and per-subject correlation — the rule that finds credential stuffing against one
///         account — would group everything under nothing and raise nothing, ever.
///     </para>
///     <para>
///         <b>Return the key, not the reference.</b> The bridge resolves the key through
///         <see cref="Pragmatic.Privacy.ObservedIdentityResolver" />, which looks up and never
///         allocates. That invariant is the reason this interface hands back a key: an implementation
///         that returned a reference could allocate one, and pseudonymising an address a stranger typed
///         would let anyone fill the subject registry by guessing.
///     </para>
///     <para>
///         <b>Answer null when the identity is not one of yours.</b> That is the ordinary case — an
///         attempt against an account that does not exist — and the entry is still written, with no
///         subject. An unattributable failure is a security signal, and often the more interesting one.
///     </para>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface ISecuritySubjectLocator
{
    /// <summary>
    ///     The subject key for <paramref name="identity" />, or <see langword="null" /> when this
    ///     application knows no subject by it.
    /// </summary>
    /// <param name="identity">The value the event carried.</param>
    /// <param name="kind">Which form <paramref name="identity" /> is in.</param>
    /// <param name="ct">The cancellation token.</param>
    ValueTask<SecuritySubjectKey?> LocateAsync(
        string identity, LoginIdentityKind kind, CancellationToken ct = default);
}
