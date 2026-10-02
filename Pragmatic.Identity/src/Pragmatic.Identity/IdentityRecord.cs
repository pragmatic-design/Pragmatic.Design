using Pragmatic.Persistence.Entity;
using Pragmatic.Privacy;

namespace Pragmatic.Identity;

/// <summary>
///     Base for all identity records (Local, Keycloak, EntraID, etc.).
///     This is NOT the domain User — the Identity is a separate infrastructure concept
///     that links to a domain User entity via ID or owned property composition.
/// </summary>
/// <remarks>
///     Provider packages (e.g., <c>Pragmatic.Identity.Local</c>) extend this class
///     with provider-specific fields (password hash, external user ID, etc.).
///     The SG composes the provider's identity record into the domain User entity
///     via EF Core OwnsOne (flat) or FK (separate table) based on the user's declaration.
/// </remarks>
public abstract class IdentityRecord : IAuditable
{
    /// <summary>
    ///     Provider-specific unique key (e.g., "{issuer}|{subject}").
    ///     Used for external identity correlation.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>It holds the sign-in address.</b> For the local provider the subject half of the
    ///     composed key <em>is</em> the address, which is why Time off's erasure test finds this column
    ///     among the three that say who an employee was. It is classified here, on the base class that
    ///     declares it, because both walks read what a type declares: classified nowhere else, it would
    ///     be masked by nothing.
    ///     <para>
    ///         <c>Anonymize</c> and not <c>Null</c>: the column cannot take null, and a plan that
    ///         assigned one would fail at the moment somebody exercises a right.
    ///     </para>
    /// </remarks>
    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
    public string ExternalIdentityKey { get; set; } = string.Empty;

    /// <summary>How this identity was provisioned.</summary>
    public ProvisionSource ProvisionSource { get; set; } = ProvisionSource.Manual;

    /// <summary>Whether this identity is currently active. Inactive identities cannot authenticate.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Last time the user authenticated via this identity.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     A <b>subject reference</b>, never an identity: the link between it and a person lives in the
    ///     subject registry, and erasing that person breaks it — which is the property the indirection
    ///     exists for, and what lets an append-only stamp outlive an erasure.
    /// </remarks>
    [NotPersonalData(
        "A subject reference. The link to a person is held by the subject registry and an erasure "
        + "breaks it, which is why an audit stamp is a reference and not a name.")]
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    /// <remarks>A subject reference, for the reason on <see cref="CreatedBy" />.</remarks>
    [NotPersonalData(
        "A subject reference. The link to a person is held by the subject registry and an erasure "
        + "breaks it, which is why an audit stamp is a reference and not a name.")]
    public string? UpdatedBy { get; set; }
}
