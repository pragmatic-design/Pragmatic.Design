namespace Pragmatic.Testing.Assertions;

/// <summary>Assertions for <see cref="bool"/> — the second most used family in this repository.</summary>
public sealed class BooleanAssertions : SubjectAssertions<bool?, BooleanAssertions>
{
    internal BooleanAssertions(bool? subject, string? expression) : base(subject, expression) { }

    /// <summary>Fails unless the subject is true.</summary>
    public AndConstraint<BooleanAssertions> BeTrue(string? because = null, params object[] becauseArgs)
    {
        if (Subject != true)
            AssertionFailure.Throw(Expression, "to be true",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is false.</summary>
    public AndConstraint<BooleanAssertions> BeFalse(string? because = null, params object[] becauseArgs)
    {
        if (Subject != false)
            AssertionFailure.Throw(Expression, "to be false",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject equals <paramref name="expected"/>.</summary>
    /// <remarks>
    ///     An overload taking a non-nullable <see cref="bool"/>, so <c>Should().Be(flag)</c> does not
    ///     have to lift its argument.
    /// </remarks>
    public AndConstraint<BooleanAssertions> Be(bool expected, string? because = null, params object[] becauseArgs) =>
        Be((bool?)expected, because, becauseArgs);
}
