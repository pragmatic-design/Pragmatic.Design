namespace Pragmatic.Testing.Assertions;

/// <summary>
///     What every assertion can do, whatever it is asserting on: equality, nullity, type.
/// </summary>
/// <remarks>
///     <para>
///         <typeparamref name="TSelf"/> is the derived assertion type, so <c>And</c> hands back the
///         specific one and a chain keeps its vocabulary:
///         <c>text.Should().NotBeNull().And.Contain("x")</c> stays on the string assertions rather
///         than falling back to the general ones.
///     </para>
///     <para>
///         <see cref="Subject"/> is public because callers reach for it —
///         <c>act.Should().Throw&lt;IOException&gt;().Subject</c> — and because a custom assertion
///         written elsewhere needs the value it is asserting on.
///     </para>
/// </remarks>
/// <typeparam name="TSubject">The type under test.</typeparam>
/// <typeparam name="TSelf">The concrete assertion type.</typeparam>
public abstract class SubjectAssertions<TSubject, TSelf>
    where TSelf : SubjectAssertions<TSubject, TSelf>
{
    private protected SubjectAssertions(TSubject subject, string? expression)
    {
        Subject = subject;
        Expression = expression;
    }

    /// <summary>The value under test.</summary>
    public TSubject Subject { get; }

    /// <summary>How the caller spelled the subject, for the failure message.</summary>
    private protected string? Expression { get; }

    /// <summary>
    ///     The same, readable from outside — a generated or hand-written assertion in another
    ///     assembly needs it to produce a message that names what the caller wrote.
    /// </summary>
    public string? SubjectExpression => Expression;

    private protected TSelf Self => (TSelf)this;

    private protected AndConstraint<TSelf> Ok() => new(Self);

    /// <summary>Fails unless the subject equals <paramref name="expected"/>.</summary>
    public AndConstraint<TSelf> Be(TSubject expected, string? because = null, params object[] becauseArgs)
    {
        if (!AreEqual(Subject, expected))
            AssertionFailure.Throw(Expression, $"to be {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>
    ///     Equality, plainly: <see cref="object.Equals(object,object)"/>, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A boxed <c>42L</c> does not equal a boxed <c>42</c>, and this says so. Comparing numbers
    ///         across types reads as a convenience and behaves as a blindfold: an assertion that a parser
    ///         produced <c>42</c> passes while the parser produces <c>42.0</c>.
    ///     </para>
    ///     <para>
    ///         A failure that reads "Expected 42, but found 42" is unhelpful, but it is pointing at
    ///         something real. The fix belongs in whatever produced the wrong type, or in an
    ///         assertion that names the type it expects — not here.
    ///     </para>
    /// </remarks>
    private protected static bool AreEqual(object? actual, object? expected) => Equals(actual, expected);

    /// <summary>Fails when the subject equals <paramref name="unexpected"/>.</summary>
    public AndConstraint<TSelf> NotBe(TSubject unexpected, string? because = null, params object[] becauseArgs)
    {
        if (AreEqual(Subject, unexpected))
            AssertionFailure.Throw(Expression, $"not to be {AssertionFailure.Format(unexpected)}",
                "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is null.</summary>
    public AndConstraint<TSelf> BeNull(string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null)
            AssertionFailure.Throw(Expression, "to be null",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject is null.</summary>
    public AndConstraint<TSelf> NotBeNull(string? because = null, params object[] becauseArgs)
    {
        if (Subject is null)
            AssertionFailure.Throw(Expression, "not to be null", "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is the very same instance as <paramref name="expected"/>.</summary>
    public AndConstraint<TSelf> BeSameAs(object? expected, string? because = null, params object[] becauseArgs)
    {
        if (!ReferenceEquals(Subject, expected))
            AssertionFailure.Throw(Expression, "to be the same instance",
                "they are different instances", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject is the very same instance as <paramref name="unexpected"/>.</summary>
    public AndConstraint<TSelf> NotBeSameAs(object? unexpected, string? because = null, params object[] becauseArgs)
    {
        if (ReferenceEquals(Subject, unexpected))
            AssertionFailure.Throw(Expression, "not to be the same instance",
                "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject's runtime type is exactly <typeparamref name="T"/>.</summary>
    /// <remarks>
    ///     Exactly, not "assignable to" — a derived type fails here, and
    ///     <see cref="BeAssignableTo{T}"/> is the one that accepts it. The returned constraint
    ///     carries the subject typed as <typeparamref name="T"/>, so <c>.Which</c> continues on it.
    /// </remarks>
    public AndWhichConstraint<TSelf, T> BeOfType<T>(string? because = null, params object[] becauseArgs)
    {
        if (Subject is not T typed || Subject.GetType() != typeof(T))
        {
            AssertionFailure.Throw(Expression, $"to be of type {typeof(T).Name}",
                Subject is null ? "found <null>" : $"found {Subject.GetType().Name}", because, becauseArgs);
            typed = default!;
        }

        return new AndWhichConstraint<TSelf, T>(Self, typed);
    }

    /// <summary>Fails unless the subject's runtime type is exactly <paramref name="expected"/>.</summary>
    /// <remarks>The non-generic form, for when the expected type is only known at run time.</remarks>
    public AndConstraint<TSelf> BeOfType(Type expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject?.GetType() != expected)
            AssertionFailure.Throw(Expression, $"to be of type {expected.Name}",
                Subject is null ? "found <null>" : $"found {Subject.GetType().Name}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject's runtime type is exactly <typeparamref name="T"/>.</summary>
    public AndConstraint<TSelf> NotBeOfType<T>(string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && Subject.GetType() == typeof(T))
            AssertionFailure.Throw(Expression, $"not to be of type {typeof(T).Name}",
                "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject can be assigned to <typeparamref name="T"/>.</summary>
    public AndWhichConstraint<TSelf, T> BeAssignableTo<T>(string? because = null, params object[] becauseArgs)
    {
        if (Subject is not T typed)
        {
            AssertionFailure.Throw(Expression, $"to be assignable to {typeof(T).Name}",
                Subject is null ? "found <null>" : $"found {Subject.GetType().Name}", because, becauseArgs);
            typed = default!;
        }

        return new AndWhichConstraint<TSelf, T>(Self, typed);
    }

    /// <summary>Fails unless the subject can be assigned to <paramref name="expected"/>.</summary>
    public AndConstraint<TSelf> BeAssignableTo(Type expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !expected.IsInstanceOfType(Subject))
            AssertionFailure.Throw(Expression, $"to be assignable to {expected.Name}",
                Subject is null ? "found <null>" : $"found {Subject.GetType().Name}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is a <typeparamref name="T"/> satisfying <paramref name="predicate"/>.</summary>
    /// <remarks>The cast and the check in one step, which is how a chain reaches a derived type.</remarks>
    public AndConstraint<TSelf> Match<T>(Func<T, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (Subject is not T typed)
            AssertionFailure.Throw(Expression, $"to be a {typeof(T).Name}",
                Subject is null ? "found <null>" : $"found {Subject.GetType().Name}", because, becauseArgs);
        else if (!predicate(typed))
            AssertionFailure.Throw(Expression, "to match the predicate",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject equals one of <paramref name="validValues"/>.</summary>
    public AndConstraint<TSelf> BeOneOf(params TSubject[] validValues) => BeOneOf(validValues, null);

    /// <summary>Fails unless the subject equals one of <paramref name="validValues"/>.</summary>
    public AndConstraint<TSelf> BeOneOf(
        IEnumerable<TSubject> validValues,
        string? because = null,
        params object[] becauseArgs)
    {
        var candidates = validValues as ICollection<TSubject> ?? [.. validValues];

        if (!candidates.Any(v => Equals(Subject, v)))
            AssertionFailure.Throw(Expression, $"to be one of {AssertionFailure.Format(candidates)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject satisfies <paramref name="predicate"/>.</summary>
    public AndConstraint<TSelf> Match(Func<TSubject, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (!predicate(Subject))
            AssertionFailure.Throw(Expression, "to match the predicate",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless <paramref name="action"/> completes without throwing.</summary>
    /// <remarks>
    ///     The callers use it to assert several things about one value at once, which is what an
    ///     xunit <c>Assert.All</c> body would otherwise hold.
    /// </remarks>
    public AndConstraint<TSelf> Satisfy(Action<TSubject> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action(Subject);
        return Ok();
    }
}
