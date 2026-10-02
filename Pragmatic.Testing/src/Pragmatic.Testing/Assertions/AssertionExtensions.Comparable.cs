using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary>
///     <c>Should()</c> for the ordered types, split off because there is one overload per type.
/// </summary>
/// <remarks>
///     One generic <c>Should&lt;T&gt;(this T) where T : IComparable&lt;T&gt;</c> would be shorter,
///     but <c>string</c> and <c>bool</c> satisfy that constraint too and would be pulled away from
///     their own families. Listing the types keeps each one where it belongs.
/// </remarks>
public static class ComparableAssertionExtensions
{
    /// <summary>Assertions for an <see cref="int"/>.</summary>
    public static ComparableAssertions<int> Should(
        this int subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="long"/>.</summary>
    public static ComparableAssertions<long> Should(
        this long subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="double"/>.</summary>
    public static ComparableAssertions<double> Should(
        this double subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="decimal"/>.</summary>
    public static ComparableAssertions<decimal> Should(
        this decimal subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="float"/>.</summary>
    public static ComparableAssertions<float> Should(
        this float subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="DateTime"/>.</summary>
    public static ComparableAssertions<DateTime> Should(
        this DateTime subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="DateTimeOffset"/>.</summary>
    public static ComparableAssertions<DateTimeOffset> Should(
        this DateTimeOffset subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="TimeSpan"/>.</summary>
    public static ComparableAssertions<TimeSpan> Should(
        this TimeSpan subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="DateOnly"/>.</summary>
    public static ComparableAssertions<DateOnly> Should(
        this DateOnly subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="TimeOnly"/>.</summary>
    public static ComparableAssertions<TimeOnly> Should(
        this TimeOnly subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);
}
