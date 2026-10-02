namespace Pragmatic.Privacy;

/// <summary>
///     Maps between a person's identity and the opaque reference the rest of the system uses for them.
/// </summary>
/// <remarks>
///     <para>
///         This indirection is what lets an audit trail survive an erasure. Entries reference a subject
///         by its pseudonym; erasing the person removes the mapping, and the entries stay complete and
///         verifiable while ceasing to be linkable to anyone.
///     </para>
///     <para>
///         Everything downstream — the trail, the key store — takes the reference and never the
///         identity. That is not a convention to remember: those components have no parameter to pass an
///         identity to.
///     </para>
/// </remarks>
public interface ISubjectRegistry
{
    /// <summary>
    ///     Returns the reference for a person, allocating one on first sight.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>An identity that was erased and comes back gets a new reference, and is not recognised
    ///         as the person it used to be.</b> Recognising it would require keeping something derived
    ///         from the identity — a hash, an index, anything answering "was this person here before" —
    ///         and keeping that is still processing their data, so the erasure would not have been one.
    ///         Continuity loses to the property the indirection exists for.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Call it when the person enters the system, not when they first act.</b> It is
    ///         idempotent, so calling it later costs a lookup — but a person who has no reference yet
    ///         cannot be named by anything that records what happens to them. An account nobody has
    ///         used is exactly the one an attacker works on, and the burst of failed sign-ins against it
    ///         is written with no subject, so a per-subject rule cannot see it. The place to call this
    ///         is the registration that creates the person, and the moment it is called is a decision
    ///         about what can be attributed, not about performance.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Never on input a stranger supplied.</b> Allocating for a typed-in address lets
    ///         anyone fill this registry by guessing, and makes the application create personal data
    ///         about people as a side effect of rejecting them. Reading paths use
    ///         <see cref="FindReferenceAsync" />, which is why
    ///         <c>ObservedIdentityResolver</c> never calls this one.
    ///     </para>
    /// </remarks>
    ValueTask<string> GetOrCreateReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default);

    /// <summary>Finds an existing reference, or null when the person is unknown or was erased.</summary>
    ValueTask<string?> FindReferenceAsync(
        string subjectType, string identifier, CancellationToken ct = default);

    /// <summary>
    ///     Resolves a reference back to the identity behind it, or null once that person has been erased.
    /// </summary>
    /// <remarks>
    ///     Returning null after an erasure is the observable proof that the link is gone: the reference
    ///     still appears throughout the trail, and nothing can turn it back into a person.
    /// </remarks>
    ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default);

    /// <summary>
    ///     Breaks the link between a reference and the person behind it, permanently.
    /// </summary>
    /// <returns>False when the reference is unknown or was already forgotten.</returns>
    ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default);
}
