// =============================================================================
// Pragmatic.Temporal.Analyzers - Diagnostic Descriptors
// =============================================================================

using Microsoft.CodeAnalysis;

namespace Pragmatic.Temporal.Analyzers;

/// <summary>
///     Diagnostic descriptors for Pragmatic.Temporal analyzers.
///     Range: PRAG0900-0999
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Pragmatic.Temporal";

    /// <summary>
    ///     PRAG0900: Avoid DateTime.Now - use IClock or TimeProvider instead.
    /// </summary>
    /// <remarks>
    ///     Both names in the message have to exist and have to be reachable. The analyzer reaches
    ///     projects that do not reference Pragmatic.Temporal and therefore cannot name <c>IClock</c>, so
    ///     the message also names <c>TimeProvider</c>, which is in the BCL and always available — not a
    ///     name such as <c>ITimeProvider</c>, which is a type in neither this framework nor the BCL.
    /// </remarks>
    public static readonly DiagnosticDescriptor AvoidDateTimeNow = new(
        id: "PRAG0900",
        title: "Avoid DateTime.Now",
        messageFormat: "Avoid using '{0}' directly; inject IClock (Pragmatic.Temporal) or TimeProvider (BCL) for testable time operations",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct use of DateTime.Now, DateTime.UtcNow, DateTimeOffset.Now, or DateTimeOffset.UtcNow makes code difficult to test. Inject Pragmatic.Temporal's IClock, or TimeProvider from the BCL when that package is not referenced. A third option is to take the instant as a parameter: an operation that is handed the moment it acts on needs no clock at all.");

    /// <summary>
    ///     PRAG0901: Avoid DateTime.Today - use IClock.Today instead.
    /// </summary>
    public static readonly DiagnosticDescriptor AvoidDateTimeToday = new(
        id: "PRAG0901",
        title: "Avoid DateTime.Today",
        messageFormat: "Avoid using 'DateTime.Today' directly; use IClock.Today (Pragmatic.Temporal) or TimeProvider for testable date operations",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct use of DateTime.Today makes code difficult to test. Use Pragmatic.Temporal's IClock.Today, which returns a DateOnly, or derive the date from TimeProvider when that package is not referenced.");

    /// <summary>
    ///     PRAG0902: Avoid new DateTime() without specifying DateTimeKind.
    /// </summary>
    public static readonly DiagnosticDescriptor AvoidDateTimeWithoutKind = new(
        id: "PRAG0902",
        title: "DateTime created without DateTimeKind",
        messageFormat: "Avoid creating DateTime without specifying DateTimeKind; use an overload with DateTimeKind or use DateTimeOffset instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Creating a DateTime without specifying DateTimeKind results in Kind == Unspecified, which can cause subtle bugs when converting between time zones. Use an overload that accepts DateTimeKind or use DateTimeOffset instead.");

    /// <summary>
    ///     PRAG0903: Avoid comparing DateTimeOffset with relational operators directly.
    /// </summary>
    public static readonly DiagnosticDescriptor AvoidDateTimeOffsetDirectComparison = new(
        id: "PRAG0903",
        title: "DateTimeOffset compared with relational operators",
        messageFormat: "Avoid comparing DateTimeOffset with '{0}' directly; use .UtcDateTime or .ToUniversalTime() before comparing to avoid timezone-dependent results",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Comparing DateTimeOffset values with <, >, <=, >= operators uses the default comparison which may produce unexpected results across different offsets. Convert to UtcDateTime or use ToUniversalTime() before comparing.");

    /// <summary>
    ///     PRAG0904: Avoid DateTime.Now/UtcNow in test code — use TestClock instead.
    /// </summary>
    public static readonly DiagnosticDescriptor AvoidDateTimeNowInTests = new(
        id: "PRAG0904",
        title: "DateTime.Now used in test code",
        messageFormat: "Avoid using '{0}' in test code; use IClock or TestClock from Pragmatic.Temporal instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Using DateTime.Now or DateTime.UtcNow in test code leads to flaky, time-dependent tests. Use Pragmatic.Temporal's IClock/TestClock for deterministic time in tests.");
}
