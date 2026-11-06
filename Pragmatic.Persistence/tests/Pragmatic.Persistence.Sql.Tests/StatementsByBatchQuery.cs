using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The statements of a batch, sorted by key (#564).</summary>
[Query<Statement>]
public partial class StatementsByBatchQuery
{
    public string? Batch { get; init; }

    [Sort]
    public SortDirection? IdSort { get; init; }
}
