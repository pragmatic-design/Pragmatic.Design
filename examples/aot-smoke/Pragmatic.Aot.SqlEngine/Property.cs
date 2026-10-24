using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A property: a tenant's, shared by scope, with its reviews.</summary>
[Entity]
[HasAccessScopes]
[Relation.OneToMany<Review>]
public partial class Property : IEntity, ITenantEntity
{
    public string Name { get; set; } = "";

    public string TenantId { get; set; } = "";
}
