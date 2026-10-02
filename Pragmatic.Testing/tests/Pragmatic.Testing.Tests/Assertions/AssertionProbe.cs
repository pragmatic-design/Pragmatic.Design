namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>
///     Runs an assertion that is expected to fail, and fails if it does not.
/// </summary>
/// <remarks>
///     Every assertion needs testing in both directions. The passing case alone would leave an
///     assertion that checks nothing indistinguishable from one that works — and an assertion that
///     never fails is worse than no assertion, because it reports a guarantee nobody is holding.
/// </remarks>
internal static class AssertionProbe
{
    /// <summary>Invokes <paramref name="assertion"/> and returns the failure it raised.</summary>
    /// <exception cref="PragmaticTestAssertionException">
    ///     When the assertion did not fail — which is itself the defect being looked for.
    /// </exception>
    public static PragmaticTestAssertionException Fails(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        try
        {
            assertion();
        }
        catch (PragmaticTestAssertionException expected)
        {
            return expected;
        }

        throw new PragmaticTestAssertionException(
            "the assertion was expected to fail, and did not — it is not checking anything");
    }
}
