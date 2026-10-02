namespace Pragmatic.Testing.Assertions;

/// <summary>Assertions for flag enums, which the general equality check cannot express.</summary>
/// <remarks>
///     Extensions on <see cref="ComparableAssertions{T}"/>, which is where an enum lands: it is a
///     value type, and the value-type overload of <c>Should()</c> claims it.
/// </remarks>
public static class EnumAssertionExtensions
{
    /// <summary>Fails unless the subject has <paramref name="expected"/> set.</summary>
    /// <remarks>
    ///     <c>Be</c> would demand the value be <b>only</b> that flag. A combination such as
    ///     <c>Class | Struct</c> has each of them set and equals neither, which is the distinction
    ///     this exists for.
    /// </remarks>
    public static AndConstraint<ComparableAssertions<TEnum>> HaveFlag<TEnum>(
        this ComparableAssertions<TEnum> assertions,
        TEnum expected,
        string? because = null,
        params object[] becauseArgs)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(assertions);

        if (!assertions.Subject.HasFlag(expected))
            AssertionFailure.Throw(null, $"to have the flag {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(assertions.Subject)}", because, becauseArgs);

        return new AndConstraint<ComparableAssertions<TEnum>>(assertions);
    }

    /// <summary>Fails when the subject has <paramref name="unexpected"/> set.</summary>
    public static AndConstraint<ComparableAssertions<TEnum>> NotHaveFlag<TEnum>(
        this ComparableAssertions<TEnum> assertions,
        TEnum unexpected,
        string? because = null,
        params object[] becauseArgs)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(assertions);

        if (assertions.Subject.HasFlag(unexpected))
            AssertionFailure.Throw(null, $"not to have the flag {AssertionFailure.Format(unexpected)}",
                "it did", because, becauseArgs);

        return new AndConstraint<ComparableAssertions<TEnum>>(assertions);
    }
}
