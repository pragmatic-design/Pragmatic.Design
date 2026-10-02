namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Assertions about what an exception carries, once one has been caught by
///     <see cref="ActionAssertions"/>.
/// </summary>
/// <typeparam name="TException">The exception type that was expected.</typeparam>
public sealed class ExceptionAssertions<TException>
    : SubjectAssertions<TException, ExceptionAssertions<TException>>
    where TException : Exception
{
    internal ExceptionAssertions(TException subject, string? expression) : base(subject, expression) { }

    /// <summary>
    ///     The exception, for a check the vocabulary here does not cover:
    ///     <c>act.Should().Throw&lt;T&gt;().Which.Code.Should().Be(…)</c>.
    /// </summary>
    public TException Which => Subject;

    /// <summary>The parameter the exception names, when it is an argument exception.</summary>
    public string? ParamName => (Subject as ArgumentException)?.ParamName;

    /// <summary>The assertions again, so several checks read as one sentence.</summary>
    public ExceptionAssertions<TException> And => this;

    /// <summary>
    ///     Fails unless the exception names <paramref name="expected"/> as the offending parameter.
    /// </summary>
    /// <remarks>
    ///     Only an <see cref="ArgumentException"/> carries one; anything else fails the check
    ///     rather than passing it silently, since asking the question at all means the caller
    ///     expected an argument exception.
    /// </remarks>
    public ExceptionAssertions<TException> WithParameterName(
        string expected, string? because = null, params object[] becauseArgs)
    {
        var actual = (Subject as ArgumentException)?.ParamName;

        if (actual != expected)
            AssertionFailure.Throw(Expression ?? "the exception",
                $"to name parameter {AssertionFailure.Format(expected)}",
                Subject is ArgumentException
                    ? $"found {AssertionFailure.Format(actual)}"
                    : $"{Subject?.GetType().Name} carries no parameter name",
                because, becauseArgs);

        return this;
    }

    /// <summary>
    ///     Fails unless the message matches <paramref name="wildcardPattern"/>, where <c>*</c> stands
    ///     for anything.
    /// </summary>
    /// <remarks>
    ///     Wildcards, not equality, because that is how the callers write it — <c>"*exceeds*"</c> —
    ///     and it keeps a test from pinning wording that is free to change.
    /// </remarks>
    public ExceptionAssertions<TException> WithMessage(
        string wildcardPattern, string? because = null, params object[] becauseArgs)
    {
        var message = Subject?.Message ?? string.Empty;

        if (!StringAssertions.MatchesWildcard(message, wildcardPattern))
            AssertionFailure.Throw(Expression ?? "the exception message",
                $"to match {AssertionFailure.Format(wildcardPattern)}",
                $"found {AssertionFailure.Format(message)}", because, becauseArgs);

        return this;
    }

    /// <summary>Fails unless an inner exception of type <typeparamref name="TInner"/> is present.</summary>
    public AndWhichConstraint<ExceptionAssertions<TException>, TInner> WithInnerException<TInner>(
        string? because = null, params object[] becauseArgs)
        where TInner : Exception
    {
        var inner = Subject?.InnerException as TInner;

        if (inner is null)
            AssertionFailure.Throw(Expression ?? "the exception",
                $"to carry an inner {typeof(TInner).Name}",
                Subject?.InnerException is null
                    ? "there was none"
                    : $"found {Subject.InnerException.GetType().Name}",
                because, becauseArgs);

        return new AndWhichConstraint<ExceptionAssertions<TException>, TInner>(Self, inner!);
    }

    /// <summary>Fails unless <paramref name="predicate"/> holds for the exception.</summary>
    public ExceptionAssertions<TException> Where(
        Func<TException, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (!predicate(Subject))
            AssertionFailure.Throw(Expression ?? "the exception", "to match the predicate",
                $"found {AssertionFailure.Format(Subject?.Message)}", because, becauseArgs);

        return this;
    }
}
