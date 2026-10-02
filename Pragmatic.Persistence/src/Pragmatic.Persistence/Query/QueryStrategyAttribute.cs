namespace Pragmatic.Persistence.Query;

/// <summary>
///     Declares how a <c>[Query]</c> reads: tracking, and whether the automatic filters apply.
/// </summary>
/// <remarks>
///     <para>
///         The generator reads it where the queryable is built, which is the only place a strategy can
///         be applied: the generated GET handler of a <c>[Endpoint]</c> query, and the read contract of
///         a <c>[Published]</c> one, which calls the repository's
///         <c>Query(QueryStrategy)</c> overload instead of the parameterless <c>Query()</c>.
///     </para>
///     <para>
///         <c>Entity</c> and <c>Filtered</c> read the way a query reads without the attribute — tracked,
///         with the filters. <c>Projection</c> drops the tracking, for a read whose rows are never
///         written back. <c>Raw</c> drops both, and dropping the filters takes tenant isolation and soft
///         delete with them: it is the same lift <c>[FilterMode]</c> and <c>[WithoutFilter&lt;T&gt;]</c>
///         ask for, and it belongs to an export or an admin panel, not to a route a tenant calls.
///     </para>
///     <para>
///         Nothing reads it on a mutation. A write loads its row tracked — it has to, to save it — and
///         the filters a write lifts are declared with <c>[FilterMode]</c> or
///         <c>[WithoutFilter&lt;T&gt;]</c>, which say it per filter rather than all at once.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [QueryStrategy(Strategy = QueryStrategy.Raw)]
/// [Query&lt;Invoice, InvoiceExportRow&gt;]
/// [Endpoint(HttpVerb.Get, "/admin/invoices/export")]
/// public partial class ExportInvoicesQuery { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class QueryStrategyAttribute : Attribute
{
    /// <summary>
    ///     The query strategy to use. Defaults to <c>QueryStrategy.Entity</c> — tracked, filtered, the
    ///     same as declaring nothing.
    /// </summary>
    public QueryStrategy Strategy { get; init; } = QueryStrategy.Entity;
}
