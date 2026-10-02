namespace Pragmatic.Privacy.EFCore.Entities;

/// <summary>
///     The link between a person and the opaque reference the rest of the system knows them by.
/// </summary>
/// <remarks>
///     Erasing a person clears <see cref="LookupIndex" /> and <see cref="Identifier" /> and stamps
///     <see cref="ForgottenAt" />, leaving the row behind. What remains records that the reference
///     existed and was erased — which is what an audit trail full of that reference needs in order to
///     stay meaningful — while holding nothing that leads back to a person.
/// </remarks>
public sealed class SubjectRecord
{
    /// <summary>The opaque reference. Random, never derived from the identity.</summary>
    public required string SubjectRef { get; set; }

    /// <summary>What kind of subject this is — the same person in two roles is two subjects.</summary>
    public required string SubjectType { get; set; }

    /// <summary>
    ///     Keyed hash of (type, identity), so a subject can be found without anything searchable about
    ///     them being stored. Null once erased, which is what stops a returning identity from being
    ///     matched back to a reference that has been reported as erased.
    /// </summary>
    public byte[]? LookupIndex { get; set; }

    /// <summary>The encrypted identity. Null once erased.</summary>
    public byte[]? Identifier { get; set; }

    /// <summary>When the reference was allocated.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the link was broken; null while the subject is known.</summary>
    public DateTimeOffset? ForgottenAt { get; set; }
}
