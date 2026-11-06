using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The statements of one code as summaries, a declared projection that formats and converts (#563).</summary>
[Query<Statement, StatementSummary>]
public partial class StatementSummariesQuery
{
    public string? Code { get; init; }
}
