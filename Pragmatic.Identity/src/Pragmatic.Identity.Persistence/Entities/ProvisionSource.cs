namespace Pragmatic.Identity.Persistence.Entities;

/// <summary>
///     How a user account was created/provisioned.
/// </summary>
public enum ProvisionSource
{
    /// <summary>Created manually by an admin or API call.</summary>
    Manual,

    /// <summary>Provisioned via JIT (Just-In-Time) on first login.</summary>
    Jit,

    /// <summary>Provisioned via SCIM (System for Cross-domain Identity Management).</summary>
    Scim
}
