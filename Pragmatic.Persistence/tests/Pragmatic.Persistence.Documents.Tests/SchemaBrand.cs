using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A tenant's brand, its code the domain key (<c>[LogicKey]</c>, #607): unique within the tenant as on EF Core and the Sql engine, so on
///     Cosmos DB a unique key within the /TenantId partition.
/// </summary>
[Entity]
public partial class SchemaBrand : IEntity, ITenantEntity
{
    [LogicKey]
    public string Code { get; set; } = "";

    public string TenantId { get; set; } = "";
}
