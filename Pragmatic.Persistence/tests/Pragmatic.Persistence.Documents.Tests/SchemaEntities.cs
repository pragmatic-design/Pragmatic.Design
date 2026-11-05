using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>A tag whose code is unique among the live ones: a partial unique index on MongoDB; on Cosmos DB a unique key it cannot be.</summary>
[Entity]
[SoftDelete]
[Unique(nameof(Code))]
public partial class SchemaTag : IEntity
{
    public string Code { get; set; } = "";
}

/// <summary>
///     A tenant's product, its SKU unique within the tenant: on Cosmos DB a unique key within the /TenantId partition. Indexed by name and
///     rank too (<c>[Index]</c>, #524): not unique, the columns as written.
/// </summary>
[Entity]
[Unique(nameof(Sku))]
[Index(nameof(Name), nameof(Rank))]
public partial class SchemaProduct : IEntity, ITenantEntity
{
    public string Sku { get; set; } = "";

    public string Name { get; set; } = "";

    public int Rank { get; set; }

    public string TenantId { get; set; } = "";
}

/// <summary>Products by name, then by rank descending: an ordering on two keys, a composite index on Cosmos DB.</summary>
[Query<SchemaProduct>]
public partial class SchemaProductsQuery
{
    [Sort(DefaultDirection = SortDirection.Ascending, Priority = 1)]
    public SortDirection? NameSort { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending, Priority = 2)]
    public SortDirection? RankSort { get; init; }
}
