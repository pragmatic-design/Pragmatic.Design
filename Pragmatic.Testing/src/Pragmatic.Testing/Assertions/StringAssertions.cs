using System.Text.RegularExpressions;

namespace Pragmatic.Testing.Assertions;

/// <summary>Assertions for <see cref="string"/>.</summary>
public sealed class StringAssertions : SubjectAssertions<string?, StringAssertions>
{
    internal StringAssertions(string? subject, string? expression) : base(subject, expression) { }

    /// <summary>Fails unless the subject contains <paramref name="expected"/>.</summary>
    public AndConstraint<StringAssertions> Contain(string expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Subject.Contains(expected, StringComparison.Ordinal))
            AssertionFailure.Throw(Expression, $"to contain {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject contains <paramref name="unexpected"/>.</summary>
    public AndConstraint<StringAssertions> NotContain(
        string unexpected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && Subject.Contains(unexpected, StringComparison.Ordinal))
            AssertionFailure.Throw(Expression, $"not to contain {AssertionFailure.Format(unexpected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject contains <paramref name="expected"/>, ignoring case.</summary>
    public AndConstraint<StringAssertions> ContainEquivalentOf(
        string expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Subject.Contains(expected, StringComparison.OrdinalIgnoreCase))
            AssertionFailure.Throw(Expression, $"to contain {AssertionFailure.Format(expected)} (ignoring case)",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject starts with <paramref name="expected"/>.</summary>
    public AndConstraint<StringAssertions> StartWith(string expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Subject.StartsWith(expected, StringComparison.Ordinal))
            AssertionFailure.Throw(Expression, $"to start with {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject starts with <paramref name="unexpected"/>.</summary>
    public AndConstraint<StringAssertions> NotStartWith(
        string unexpected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && Subject.StartsWith(unexpected, StringComparison.Ordinal))
            AssertionFailure.Throw(Expression, $"not to start with {AssertionFailure.Format(unexpected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject ends with <paramref name="expected"/>.</summary>
    public AndConstraint<StringAssertions> EndWith(string expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Subject.EndsWith(expected, StringComparison.Ordinal))
            AssertionFailure.Throw(Expression, $"to end with {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject ends with <paramref name="unexpected"/>.</summary>
    public AndConstraint<StringAssertions> NotEndWith(
        string unexpected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && Subject.EndsWith(unexpected, StringComparison.Ordinal))
            AssertionFailure.Throw(Expression, $"not to end with {AssertionFailure.Format(unexpected)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is empty.</summary>
    public AndConstraint<StringAssertions> BeEmpty(string? because = null, params object[] becauseArgs)
    {
        if (Subject is not "")
            AssertionFailure.Throw(Expression, "to be empty",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject is empty.</summary>
    public AndConstraint<StringAssertions> NotBeEmpty(string? because = null, params object[] becauseArgs)
    {
        if (Subject is "")
            AssertionFailure.Throw(Expression, "not to be empty", "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject is null or empty.</summary>
    public AndConstraint<StringAssertions> NotBeNullOrEmpty(string? because = null, params object[] becauseArgs)
    {
        if (string.IsNullOrEmpty(Subject))
            AssertionFailure.Throw(Expression, "not to be null or empty",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is null or empty.</summary>
    public AndConstraint<StringAssertions> BeNullOrEmpty(string? because = null, params object[] becauseArgs)
    {
        if (!string.IsNullOrEmpty(Subject))
            AssertionFailure.Throw(Expression, "to be null or empty",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject is null, empty, or only whitespace.</summary>
    public AndConstraint<StringAssertions> NotBeNullOrWhiteSpace(string? because = null, params object[] becauseArgs)
    {
        if (string.IsNullOrWhiteSpace(Subject))
            AssertionFailure.Throw(Expression, "not to be null or whitespace",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject is null, empty, or only whitespace.</summary>
    public AndConstraint<StringAssertions> BeNullOrWhiteSpace(string? because = null, params object[] becauseArgs)
    {
        if (!string.IsNullOrWhiteSpace(Subject))
            AssertionFailure.Throw(Expression, "to be null or whitespace",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject has exactly <paramref name="expected"/> characters.</summary>
    public AndConstraint<StringAssertions> HaveLength(int expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject?.Length != expected)
            AssertionFailure.Throw(Expression, $"to have length {expected}",
                Subject is null ? "found <null>" : $"found {Subject.Length}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject equals <paramref name="expected"/>, ignoring case.</summary>
    public AndConstraint<StringAssertions> BeEquivalentTo(
        string? expected, string? because = null, params object[] becauseArgs)
    {
        if (!string.Equals(Subject, expected, StringComparison.OrdinalIgnoreCase))
            AssertionFailure.Throw(Expression, $"to equal {AssertionFailure.Format(expected)} (ignoring case)",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject matches <paramref name="pattern"/> as a regular expression.</summary>
    public AndConstraint<StringAssertions> MatchRegex(string pattern, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Regex.IsMatch(Subject, pattern))
            AssertionFailure.Throw(Expression, $"to match /{pattern}/",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the subject matches <paramref name="pattern"/> as a regular expression.</summary>
    public AndConstraint<StringAssertions> NotMatchRegex(
        string pattern, string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && Regex.IsMatch(Subject, pattern))
            AssertionFailure.Throw(Expression, $"not to match /{pattern}/",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject matches a wildcard pattern, where <c>*</c> stands for anything.</summary>
    /// <remarks>
    ///     The shape <c>WithMessage("*exceeds*")</c> uses, and the reason exception messages can be
    ///     asserted on without pinning their exact wording.
    /// </remarks>
    public AndConstraint<StringAssertions> Match(string wildcardPattern, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !MatchesWildcard(Subject, wildcardPattern))
            AssertionFailure.Throw(Expression, $"to match {AssertionFailure.Format(wildcardPattern)}",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Whether <paramref name="value"/> matches a <c>*</c>-wildcard pattern, case-insensitively.</summary>
    internal static bool MatchesWildcard(string value, string pattern)
    {
        var expression = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(value, expression, RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }
}
