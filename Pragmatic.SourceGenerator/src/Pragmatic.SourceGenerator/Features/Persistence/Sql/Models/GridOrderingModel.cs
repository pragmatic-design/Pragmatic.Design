using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>An engine's sort for a grid: its ordering and key types, and the compiled key of each sortable property and of the entity's key.</summary>
internal sealed record GridOrderingModel(string Ordering, string Key, EquatableArray<OrderKeyModel> Keys);
