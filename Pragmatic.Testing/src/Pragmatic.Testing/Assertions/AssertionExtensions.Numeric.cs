using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary>
///     <c>Should()</c> for the remaining ordered types: the less common integers, and the nullable
///     form of every one of them.
/// </summary>
/// <remarks>
///     Nullables need their own overloads. A <c>DateTime?</c> does not match the <c>DateTime</c>
///     overload, so without these it falls back to the object assertions — where <c>BeAfter</c> does
///     not exist, and the failure is a compile error a long way from the cause.
/// </remarks>
public static class NumericAssertionExtensions
{
    /// <summary>Assertions for a <see cref="uint"/>.</summary>
    public static ComparableAssertions<uint> Should(
        this uint subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="ulong"/>.</summary>
    public static ComparableAssertions<ulong> Should(
        this ulong subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="short"/>.</summary>
    public static ComparableAssertions<short> Should(
        this short subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="ushort"/>.</summary>
    public static ComparableAssertions<ushort> Should(
        this ushort subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a <see cref="byte"/>.</summary>
    public static ComparableAssertions<byte> Should(
        this byte subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for an <see cref="sbyte"/>.</summary>
    public static ComparableAssertions<sbyte> Should(
        this sbyte subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="int"/>.</summary>
    public static ComparableAssertions<int?> Should(
        this int? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="long"/>.</summary>
    public static ComparableAssertions<long?> Should(
        this long? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="double"/>.</summary>
    public static ComparableAssertions<double?> Should(
        this double? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="decimal"/>.</summary>
    public static ComparableAssertions<decimal?> Should(
        this decimal? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="float"/>.</summary>
    public static ComparableAssertions<float?> Should(
        this float? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="DateTime"/>.</summary>
    public static ComparableAssertions<DateTime?> Should(
        this DateTime? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="DateTimeOffset"/>.</summary>
    public static ComparableAssertions<DateTimeOffset?> Should(
        this DateTimeOffset? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="TimeSpan"/>.</summary>
    public static ComparableAssertions<TimeSpan?> Should(
        this TimeSpan? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable <see cref="DateOnly"/>.</summary>
    public static ComparableAssertions<DateOnly?> Should(
        this DateOnly? subject, [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);
}
