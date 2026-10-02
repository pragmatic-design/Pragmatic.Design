namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One <c>[Published]</c> query: the data needed to expose it on a boundary's read contract
///     (<c>I{Module}Reads</c>) and implement the contract method by delegating to the query executor (#3).
///     All type names are global-qualified.
/// </summary>
internal sealed record PublishedQueryModel
{
    /// <summary>The read-contract interface name, e.g. <c>IBillingReads</c>.</summary>
    public required string ContractName { get; init; }

    /// <summary>The namespace the contract is emitted into, e.g. <c>App.Billing.Contracts</c>.</summary>
    public required string ContractNamespace { get; init; }

    /// <summary>The contract method name, derived from the query type, e.g. <c>GetInvoices</c>.</summary>
    public required string MethodName { get; init; }

    /// <summary>The query class's own name, for a diagnostic that has to name the declaration.</summary>
    public required string QueryTypeShortName { get; init; }

    /// <summary>Where the query is declared. Excluded from equality by <see cref="Core.LocationInfo" />.</summary>
    public Core.LocationInfo? Location { get; init; }

    public required string QueryTypeFullName { get; init; }
    public required string EntityTypeFullName { get; init; }
    public required string EntityIdTypeFullName { get; init; }
    public required string EntityShortName { get; init; }
    public required string ResultTypeFullName { get; init; }

    /// <summary>
    ///     The strategy declared with <c>[QueryStrategy]</c>, or <c>null</c> when the query declares none.
    /// </summary>
    /// <remarks>
    ///     Null keeps the parameterless <c>Query()</c> the contract has always called; a value selects
    ///     the repository's <c>Query(QueryStrategy)</c> overload, which is where tracking and the
    ///     Pragmatic filter pipeline are decided.
    /// </remarks>
    public QueryStrategyKind? Strategy { get; init; }

    public bool IsValid =>
        !string.IsNullOrEmpty(ContractName) &&
        !string.IsNullOrEmpty(MethodName) &&
        !string.IsNullOrEmpty(QueryTypeFullName) &&
        !string.IsNullOrEmpty(EntityTypeFullName) &&
        !string.IsNullOrEmpty(ResultTypeFullName);
}
