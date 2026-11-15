namespace Pragmatic.MultiTenancy;

/// <summary>
///     Declares that the rows of this entity are the tenants: the generator writes an <see cref="ITenantStore" /> over it, and the host
///     registers that store, wrapped by <see cref="ObservedTenantStore" />, in place of the empty in-memory one.
/// </summary>
/// <remarks>
///     <para>
///         Without a store over the application's tenants, <c>RequireKnownTenant</c> and <c>EnforceTenantState</c> mean nothing: every
///         tenant is unknown, or no state is enforced. Each application wrote the same read view by hand.
///     </para>
///     <para>
///         <b>A read view.</b> The generated store reads the entity through its read repository, from a scope of its own on each call (the
///         store is a singleton, the repository is scoped). Its write members refuse with <see cref="NotSupportedException" />: a tenant's
///         row is written by the application's own operations, under their permissions, and a second way in would have none.
///     </para>
///     <para>
///         <b>A shared database only.</b> With a database per tenant the framework asks the store which database a tenant lives in, so the
///         store cannot read through the tenant-routed context: <c>AddDbPerTenant</c> refuses the generated store at startup. A host on a
///         database per tenant keeps a hand-written store that opens the shared connection itself.
///     </para>
///     <para>
///         One per host. <c>PRAG0642</c> reports a member that does not exist or has the wrong type; <c>PRAG1617</c> two sources in one host.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Entity]
/// [TenantSource(Id = nameof(Slug), Name = nameof(LegalName), State = nameof(State))]
/// public partial class Organization : IEntity
/// {
///     public string Slug { get; private set; } = "";
///     public string LegalName { get; private set; } = "";
///     public TenantState State { get; private set; } = TenantState.Active;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantSourceAttribute : Attribute
{
    /// <summary>The <see cref="string" /> property holding the tenant id: what a token's tenant claim and <see cref="ITenantContext.TenantId" /> carry.</summary>
    public required string Id { get; init; }

    /// <summary>The <see cref="string" /> property holding the tenant's name.</summary>
    public required string Name { get; init; }

    /// <summary>The <see cref="TenantState" /> property holding whether the tenant is served.</summary>
    public required string State { get; init; }

    /// <summary>
    ///     The <see cref="DateTimeOffset" /> property holding when the tenant was registered. Unset, the entity's <c>CreatedAt</c> when it is
    ///     <c>[Auditable]</c>, and otherwise <see cref="DateTimeOffset.MinValue" />.
    /// </summary>
    public string? CreatedAt { get; init; }
}
