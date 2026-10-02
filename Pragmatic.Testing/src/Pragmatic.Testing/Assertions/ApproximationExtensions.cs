namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Comparisons with a tolerance: floating point, and instants that were measured rather than set.
/// </summary>
/// <remarks>
///     These are extensions rather than members of <see cref="ComparableAssertions{T}"/> because
///     each needs a tolerance of a type the general family knows nothing about — a
///     <see cref="TimeSpan"/> for a date, a <see cref="double"/> for a number.
/// </remarks>
public static class ApproximationExtensions
{
    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<double>> BeApproximately(
        this ComparableAssertions<double> assertions,
        double expected,
        double precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions, Math.Abs(assertions.Subject - expected) <= precision, expected, precision,
            because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<float>> BeApproximately(
        this ComparableAssertions<float> assertions,
        float expected,
        float precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions, Math.Abs(assertions.Subject - expected) <= precision, expected, precision,
            because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<decimal>> BeApproximately(
        this ComparableAssertions<decimal> assertions,
        decimal expected,
        decimal precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions, Math.Abs(assertions.Subject - expected) <= precision, expected, precision,
            because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<DateTime>> BeCloseTo(
        this ComparableAssertions<DateTime> assertions,
        DateTime expected,
        TimeSpan precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions, (assertions.Subject - expected).Duration() <= precision, expected, precision,
            because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<DateTimeOffset>> BeCloseTo(
        this ComparableAssertions<DateTimeOffset> assertions,
        DateTimeOffset expected,
        TimeSpan precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions, (assertions.Subject - expected).Duration() <= precision, expected, precision,
            because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<DateTimeOffset?>> BeCloseTo(
        this ComparableAssertions<DateTimeOffset?> assertions,
        DateTimeOffset expected,
        TimeSpan precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions,
            assertions.Subject is { } actual && (actual - expected).Duration() <= precision,
            expected, precision, because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<DateTime?>> BeCloseTo(
        this ComparableAssertions<DateTime?> assertions,
        DateTime expected,
        TimeSpan precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions,
            assertions.Subject is { } actual && (actual - expected).Duration() <= precision,
            expected, precision, because, becauseArgs);
    }

    /// <summary>Fails unless the subject is within <paramref name="precision"/> of <paramref name="expected"/>.</summary>
    public static AndConstraint<ComparableAssertions<TimeSpan>> BeCloseTo(
        this ComparableAssertions<TimeSpan> assertions,
        TimeSpan expected,
        TimeSpan precision,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        return Within(assertions, (assertions.Subject - expected).Duration() <= precision, expected, precision,
            because, becauseArgs);
    }

    /// <summary>Throws unless <paramref name="satisfied"/>, with a message naming the tolerance.</summary>
    private static AndConstraint<ComparableAssertions<T>> Within<T>(
        ComparableAssertions<T> assertions,
        bool satisfied,
        object expected,
        object precision,
        string? because,
        object[] becauseArgs)
    {
        if (!satisfied)
            AssertionFailure.Throw(null,
                $"to be within {AssertionFailure.Format(precision)} of {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(assertions.Subject)}", because, becauseArgs);

        return new AndConstraint<ComparableAssertions<T>>(assertions);
    }
}
