namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     A grid sent a filter with more terms than the adapters accept.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The bound is about the process, not the database.</b> A composite of N terms becomes a chain of
///         N joined comparisons, and everything that reads the query walks that chain recursively: LINQ's
///         compiler, EF Core's translator, every visitor in between. Measured on .NET 10 (2026-10-19), EF
///         Core's SQL translation overflowed the stack of a thread-pool thread between 1,000 and 1,250 terms,
///         a 30 KB request body. A stack overflow is not an exception: it ends the process, so one request
///         takes down every other request the host was serving.
///     </para>
///     <para>
///         <see cref="MaxTerms" /> leaves a factor of four below that, for the frames a real request already
///         has on its stack. A term is what nests the query one level deeper: a <c>[field, op, value]</c>
///         clause, a value of an <c>in</c>, a column of a global filter, a column filter of PrimeNG, and a
///         sort descriptor, whose <c>ThenBy</c> chain overflowed the same way at 10,000.
///     </para>
///     <para>
///         Thrown rather than returned, like <see cref="GridFieldRejectedException" />: ignoring the filter
///         would answer with more rows than the client asked for. The host maps it to 400.
///     </para>
/// </remarks>
public sealed class GridFilterTooLargeException : InvalidOperationException
{
    /// <summary>The most terms a grid filter may have.</summary>
    public const int MaxTerms = 256;

    /// <summary>Creates the exception for a filter that reached <paramref name="terms" /> terms.</summary>
    public GridFilterTooLargeException(int terms)
        : base($"The filter has more than {MaxTerms} terms ({terms} counted).")
        => Terms = terms;

    /// <summary>
    ///     The terms counted when the filter was refused: past <see cref="MaxTerms" />, not necessarily all of
    ///     them, since counting stops at the first one over.
    /// </summary>
    public int Terms { get; }
}
