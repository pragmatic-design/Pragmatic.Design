using Pragmatic;

namespace Casework.Verify.Enums;

/// <summary>
///     Where an organisation is in <b>this</b> service's onboarding.
/// </summary>
/// <remarks>
///     ⚠️ The twin of Intake's, and a separate type on purpose: the two registers answer for two
///     different databases, and sharing the enum would mean sharing an assembly between the services —
///     which is the coupling this example exists not to have. What crosses is a message, and a message
///     carries the tenant id, not this.
/// </remarks>
[FastEnum]
public enum OrganisationState
{
    /// <summary>Known, and its database not made yet. This service refuses its requests meanwhile.</summary>
    Provisioning,

    /// <summary>Its database is there and migrated: verifications for it can be written.</summary>
    Active,

    /// <summary>It may no longer be used.</summary>
    Deactivated
}
