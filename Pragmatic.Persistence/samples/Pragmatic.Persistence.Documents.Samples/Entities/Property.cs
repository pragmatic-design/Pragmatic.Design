using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace PragmaticDocumentSamples.Entities;

/// <summary>A property, declared as Pragmatic declares it: a tenant's (partitioned by it on Cosmos DB), shared by scope.</summary>
[Entity]
[HasAccessScopes]
[Relation.OneToMany<Reservation>]
public partial class Property : IEntity, ITenantEntity
{
    public string Name { get; set; } = "";

    public string TenantId { get; set; } = "";
}
