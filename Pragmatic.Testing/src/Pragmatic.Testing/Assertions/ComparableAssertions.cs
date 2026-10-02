namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Assertions for anything ordered — numbers, dates, <see cref="TimeSpan"/>, any
///     <see cref="IComparable{T}"/>.
/// </summary>
/// <remarks>
///     One family rather than one per numeric type: the checks are identical once the type can
///     compare itself, and <c>Should()</c> has an overload per type to land here.
/// </remarks>
/// <typeparam name="TSubject">The ordered type under test.</typeparam>
public sealed class ComparableAssertions<TSubject>
    : SubjectAssertions<TSubject, ComparableAssertions<TSubject>>
{
    internal ComparableAssertions(TSubject subject, string? expression) : base(subject, expression) { }

    /// <summary>Fails unless the subject is greater than <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeGreaterThan(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c > 0, "to be greater than", because, becauseArgs);

    /// <summary>Fails unless the subject is greater than or equal to <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeGreaterOrEqualTo(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c >= 0, "to be at least", because, becauseArgs);

    /// <summary>Fails unless the subject is greater than or equal to <paramref name="expected"/>.</summary>
    /// <remarks>The spelling FluentAssertions 6.x introduced; both are in use in this repository.</remarks>
    public AndConstraint<ComparableAssertions<TSubject>> BeGreaterThanOrEqualTo(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        BeGreaterOrEqualTo(expected, because, becauseArgs);

    /// <summary>Fails unless the subject is less than <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeLessThan(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c < 0, "to be less than", because, becauseArgs);

    /// <summary>Fails unless the subject is less than or equal to <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeLessOrEqualTo(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c <= 0, "to be at most", because, becauseArgs);

    /// <summary>Fails unless the subject is less than or equal to <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeLessThanOrEqualTo(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        BeLessOrEqualTo(expected, because, becauseArgs);

    /// <summary>Fails unless the subject lies within <paramref name="minimum"/>..<paramref name="maximum"/>, inclusive.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeInRange(
        TSubject minimum, TSubject maximum, string? because = null, params object[] becauseArgs)
    {
        if (Compare(minimum) < 0 || Compare(maximum) > 0)
            AssertionFailure.Throw(Expression,
                $"to be between {AssertionFailure.Format(minimum)} and {AssertionFailure.Format(maximum)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is after <paramref name="expected"/> — the date spelling of greater-than.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeAfter(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c > 0, "to be after", because, becauseArgs);

    /// <summary>Fails unless the subject is at or after <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeOnOrAfter(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c >= 0, "to be on or after", because, becauseArgs);

    /// <summary>Fails unless the subject is before <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeBefore(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c < 0, "to be before", because, becauseArgs);

    /// <summary>Fails unless the subject is at or before <paramref name="expected"/>.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeOnOrBefore(
        TSubject expected, string? because = null, params object[] becauseArgs) =>
        Compare(expected, c => c <= 0, "to be on or before", because, becauseArgs);

    /// <summary>Fails unless the subject is greater than its type's default — zero, for a number.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BePositive(
        string? because = null, params object[] becauseArgs) =>
        Compare(default!, c => c > 0, "to be positive, greater than", because, becauseArgs);

    /// <summary>Fails unless the subject is less than its type's default — zero, for a number.</summary>
    public AndConstraint<ComparableAssertions<TSubject>> BeNegative(
        string? because = null, params object[] becauseArgs) =>
        Compare(default!, c => c < 0, "to be negative, less than", because, becauseArgs);

    private AndConstraint<ComparableAssertions<TSubject>> Compare(
        TSubject expected, Func<int, bool> accepted, string expectation, string? because, object[] becauseArgs)
    {
        if (!accepted(Compare(expected)))
            AssertionFailure.Throw(Expression, $"{expectation} {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>
    ///     Orders through <see cref="Comparer{T}.Default"/> rather than <see cref="IComparable{T}"/>.
    /// </summary>
    /// <remarks>
    ///     The constraint would have excluded every nullable value type — <c>int?</c> does not
    ///     implement <c>IComparable&lt;int?&gt;</c> — and each one would have fallen back to the
    ///     object assertions, where <c>BeGreaterThan</c> does not exist. The default comparer handles
    ///     them, and puts null before everything, which is the ordering the callers assume.
    /// </remarks>
    private int Compare(TSubject expected) => Comparer<TSubject>.Default.Compare(Subject, expected);
}
