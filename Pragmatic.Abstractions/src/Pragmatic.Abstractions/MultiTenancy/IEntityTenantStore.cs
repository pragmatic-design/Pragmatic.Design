namespace Pragmatic.MultiTenancy;

/// <summary>
///     An <see cref="ITenantStore" /> the generator wrote over an entity declared with <see cref="TenantSourceAttribute" />: a read view
///     through that entity's read repository.
/// </summary>
/// <remarks>
///     What lets a host tell the generated store from one an application wrote: <c>AddDbPerTenant</c> refuses it at startup, because it
///     reads through the context a database per tenant routes, which asks this store which database to open.
/// </remarks>
public interface IEntityTenantStore : ITenantStore
{
    /// <summary>The entity whose rows are the tenants.</summary>
    Type EntityType { get; }
}
