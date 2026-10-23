using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A declared query's sorts on the Sql engine, in their priority: an ordering of compiled keys, the key's last.</summary>
internal sealed record SqlOrderingModel(EquatableArray<QuerySortModel> Sorts, EquatableArray<OrderKeyModel> Keys);
