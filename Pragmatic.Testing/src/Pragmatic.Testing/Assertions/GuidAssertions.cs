using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Assertions for <see cref="Guid"/>, where "empty" means <see cref="Guid.Empty"/> rather than
///     a length of zero.
/// </summary>
public sealed class GuidAssertions : SubjectAssertions<Guid?, GuidAssertions>
{
    internal GuidAssertions(Guid? subject, string? expression) : base(subject, expression) { }

    /// <summary>Fails unless the subject is <see cref="Guid.Empty"/>.</summary>
    public AndConstraint<GuidAssertions> BeEmpty(string? because = null, params object[] becauseArgs)
    {
        if (Subject != Guid.Empty)
            AssertionFailure.Throw(Expression, "to be an empty guid",
                $"found {AssertionFailure.Format(Subject)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>
    ///     Fails when the subject is <see cref="Guid.Empty"/> — which is what an identifier nobody
    ///     assigned looks like, and the reason this is asserted so often.
    /// </summary>
    public AndConstraint<GuidAssertions> NotBeEmpty(string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || Subject == Guid.Empty)
            AssertionFailure.Throw(Expression, "not to be an empty guid",
                Subject is null ? "found <null>" : "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the subject equals <paramref name="expected"/>.</summary>
    public AndConstraint<GuidAssertions> Be(Guid expected, string? because = null, params object[] becauseArgs) =>
        Be((Guid?)expected, because, becauseArgs);

    /// <summary>Fails when the subject equals <paramref name="unexpected"/>.</summary>
    public AndConstraint<GuidAssertions> NotBe(Guid unexpected, string? because = null, params object[] becauseArgs) =>
        NotBe((Guid?)unexpected, because, becauseArgs);
}
