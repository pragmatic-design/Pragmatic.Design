using Pragmatic.Actions.Pipeline.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The order the filter stages run in.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ No test here has the form <c>FilterOrder.X.Should().Be(300)</c> — a constant compared to
///         its own literal cannot fail for any reason that matters, and it makes a constant look used
///         whether or not its stage exists. There is no transaction stage and no caching stage: the
///         transaction is opened inline by the invoker when the action is <c>[Transactional]</c>, and
///         caching is a different mechanism.
///     </para>
///     <para>
///         What is tested is the one thing that can actually be broken by editing a number: the sequence.
///         Validation before authorization is not a preference — a filter that rejects the caller should
///         not run after one that has already read their input, and permission before policy before
///         resource is the widening order from "may they do this at all" to "may they touch this row".
///     </para>
/// </remarks>
public class FilterOrderTests
{
    [Fact]
    public void ValidationRunsBeforeAnyAuthorization()
    {
        FilterOrder.Validation.Should().BeLessThan(FilterOrder.Authorization,
            "an input is checked before the caller is, so a malformed request is refused for what it is");
    }

    [Fact]
    public void TheThreeAuthorizationStagesWidenInOrder()
    {
        FilterOrder.Authorization.Should().BeLessThan(FilterOrder.PolicyEvaluation,
            "the operation permission is the cheapest question and the one that fails most often");
        FilterOrder.PolicyEvaluation.Should().BeLessThan(FilterOrder.ResourceAuthorization,
            "a declared policy is evaluated before the row is reached for");
    }

    [Fact]
    public void LoggingRunsLast()
    {
        FilterOrder.ResourceAuthorization.Should().BeLessThan(FilterOrder.Logging,
            "logging records what happened, which means everything else has to have happened");
    }
}
