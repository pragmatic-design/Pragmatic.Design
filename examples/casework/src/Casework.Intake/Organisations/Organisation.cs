using Casework.Intake.Enums;

namespace Casework.Intake.Entities;

/// <summary>
///     An organisation that brings cases: the tenant, as a row.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It is not an <c>ITenantEntity</c>, and it must not be.</b> This is the register the
///         tenant is looked up <em>in</em> — a row of it is read before anybody knows which tenant the
///         request belongs to, and a tenant filter on it would be a lookup that can only find what you
///         already know. It is the one entity in this module that every organisation's rows are not.
///     </para>
///     <para>
///         <c>DedicatedConnectionString</c> is what makes the model <b>hybrid</b>: an organisation with
///         one gets a database of its own, an organisation without stays on the shared schema with
///         row-level isolation. Both are the same deployment, and this example shows both rather than
///         picking one.
///     </para>
///     <para>
///         ⚠️ <b>Where this table lives is a decision.</b> The register has to be readable before a
///         tenant's database is known, so it is read from the <b>shared</b> database — see
///         <c>TheOrganisationsAreTheTenants</c>, which opens that connection explicitly rather than
///         going through the tenant-routed <c>DbContext</c> it would otherwise be part of. The schema
///         creates the table in every database because it is an entity of this boundary; in a dedicated
///         one it stays empty, which is the cost of not giving this example a control-plane database of
///         its own.
///     </para>
/// </remarks>
[Entity]
[Audited]
public partial class Organisation : IEntity
{
    /// <summary>The tenant id this organisation is known by — what the token's claim carries.</summary>
    [LogicKey]
    [Required]
    [MaxLength(60)]
    public string TenantKey { get; private set; } = "";

    /// <summary>What it is called, for an operator's screen.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; private set; } = "";

    /// <summary>
    ///     Its own database, or <see langword="null" /> to stay on the shared schema.
    /// </summary>
    /// <remarks>
    ///     A connection string on a row, which is what <c>TenantConnectionResolver</c> asks the store
    ///     for. ⚠️ It is a secret in a column: this example keeps it here because that is the shape the
    ///     framework's own <c>TenantInfo.ConnectionString</c> has, and <c>TenantInfo</c> excludes it from
    ///     its <c>ToString</c> for the same reason a deployment would keep it in a secret store and hand
    ///     the store a reference instead.
    /// </remarks>
    [MaxLength(500)]
    public string? DedicatedConnectionString { get; private set; }

    /// <summary>
    ///     Where it is in its onboarding — which is what decides whether a request as this organisation
    ///     is served at all.
    /// </summary>
    /// <remarks>
    ///     A state and not a flag, because onboarding crosses two services and a
    ///     boolean has no word for "Intake is ready and Verify is not". See <see cref="OrganisationState" />.
    /// </remarks>
    public OrganisationState State { get; private set; } = OrganisationState.Provisioning;

    /// <summary>Registered, and not yet able to work: the other service has still to make its own.</summary>
    internal static Organisation Onboarded(string tenantKey, string name, string? dedicatedConnectionString)
    {
        var organisation = Create();
        organisation.SetTenantKey(tenantKey);
        organisation.SetName(name);
        organisation.SetDedicatedConnectionString(dedicatedConnectionString);
        organisation.SetState(OrganisationState.Provisioning);

        return organisation;
    }
}
