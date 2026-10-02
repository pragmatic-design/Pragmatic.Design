using Casework.Verify.Enums;

namespace Casework.Verify.Entities;

/// <summary>
///     An organisation whose verifications this service holds: the tenant, as a row.
/// </summary>
/// <remarks>
///     <para>
///         The twin of Intake's, and deliberately a <b>second</b> register rather than a shared one. Each
///         service knows which organisations it serves and where their rows are; a register both read
///         would be a database two services depend on, which is the coupling this example exists to not
///         have. Onboarding keeps the two in step the only way that leaves them independent: a message.
///     </para>
///     <para>
///         ⚠️ Not an <c>ITenantEntity</c>, for the same reason as Intake's: it is the table a tenant is
///         looked up in. And read from this service's <b>shared</b> database — see
///         <c>TheOrganisationsThisServiceServes</c>.
///     </para>
/// </remarks>
[Entity]
[Audited]
public partial class Organisation : IEntity
{
    /// <summary>The tenant id — the same value Intake knows it by, and the one the message carries.</summary>
    [LogicKey]
    [Required]
    [MaxLength(60)]
    public string TenantKey { get; private set; } = "";

    [Required]
    [MaxLength(200)]
    public string Name { get; private set; } = "";

    /// <summary>Its own database for <b>this</b> service, or null to stay on the shared schema.</summary>
    /// <remarks>
    ///     ⚠️ Independent of Intake's. An organisation can have a dedicated database in one service and
    ///     share the schema in the other: the two are separate deployments of separate data, and nothing
    ///     in either connection string names the other's. Onboarding provisions each service's own,
    ///     in that service.
    /// </remarks>
    [MaxLength(500)]
    public string? DedicatedConnectionString { get; private set; }

    /// <summary>
    ///     Where it is in <b>this</b> service's onboarding.
    /// </summary>
    /// <remarks>
    ///     Its own state, not a copy of Intake's: this register answers for this service's database, and
    ///     an organisation that Intake calls ready is not ready here until the row below it exists.
    /// </remarks>
    public OrganisationState State { get; private set; } = OrganisationState.Provisioning;
}
