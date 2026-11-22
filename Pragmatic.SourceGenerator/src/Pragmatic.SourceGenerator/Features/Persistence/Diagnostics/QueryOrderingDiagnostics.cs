using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     Diagnostics about the order a query reads its rows in. <c>PRAG0746</c>, a hole in the module's range.
/// </summary>
internal static class QueryOrderingDiagnostics
{
    /// <summary>
    ///     A paged query declares no <c>[Sort]</c>, so the generated query pages in the order of the key (#938).
    /// </summary>
    /// <remarks>
    ///     Info, not a warning: the query is correct, and the order is the one that makes its pages stable. It is
    ///     said where the query is written because otherwise the order is visible only in the SQL.
    /// </remarks>
    public static readonly DiagnosticDescriptor PagedByTheKey = DiagnosticFactory.Info(
        "PRAG0746",
        "A paged query with no sort pages in the order of the key",
        "Query '{0}' is paged and declares no [Sort], so its pages are read in the order of '{1}'",
        "Without an order, Skip/Take are not stable: two requests for one page can return different rows. Declare a "
        + "[Sort] with a DefaultDirection to page in another order.");
}
