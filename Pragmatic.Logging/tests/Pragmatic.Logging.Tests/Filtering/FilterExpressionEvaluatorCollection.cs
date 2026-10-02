using Xunit;

namespace Pragmatic.Logging.Tests.Filtering;

/// <summary>
///     The test classes that touch <c>FilterExpressionEvaluator</c>'s compiled-expression cache and its
///     global statistics, which are static and shared.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Without this they run in parallel, and one class calling <c>ClearCache()</c> can land in
///         the middle of another's measured loop: the next evaluation recompiles instead of hitting the
///         cache, and a run that should take microseconds takes milliseconds.
///         <c>ExpressionIntegrationTests.ComplexExpression_PerformanceIsAcceptable</c> would fail
///         intermittently that way in a full run and pass every time alone, because alone there is no
///         second class to clear the cache under it.
///     </para>
///     <para>
///         A collection rather than a wider tolerance: the tests do not need more room, they need not to
///         be interrupted. The same reasoning applies to <c>ResetStatistics()</c> and
///         <c>GetStatistics()</c> — a hit ratio is meaningless while another class is feeding the same
///         counters.
///     </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class FilterExpressionEvaluatorCollection
{
    /// <summary>The collection name, so a class joining it does not spell a string of its own.</summary>
    public const string Name = "FilterExpressionEvaluator static cache";
}
