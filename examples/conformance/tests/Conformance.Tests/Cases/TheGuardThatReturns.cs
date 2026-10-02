using System.Net;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>Pragmatic.Ensure.Result</c>: a guard that <b>returns</b> the error instead of throwing it.
/// </summary>
/// <remarks>
///     <para>
///         The framework's two rules — <c>Result</c> over exceptions, <c>Ensure</c> for guards — meet in this
///         package. The difference is visible to the caller: <c>Ensure</c> throws, and the body of an action
///         that throws is a <b>500</b>; <c>Check</c> returns the error it is given, and here that is a 422
///         naming its rule.
///     </para>
///     <para>
///         ⚠️ The operation carries no <c>[Required]</c> on purpose: with the declarative rule the pipeline
///         would refuse first and the body would not start, so the case would measure validation — which has
///         its own cells — instead of the guard.
///     </para>
///     <para>
///         Measured by replacing <c>Check</c> with <c>Ensure.ThrowIfNullOrWhiteSpace</c>, which is the
///         comparison that matters: **1 red out of 205**, this one, because the same input becomes a fault
///         instead of a refusal. That is all the package buys, and it shows only from outside.
///     </para>
/// </remarks>
public class TheGuardThatReturns(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task TheGuardRefuses_WithItsOwnRule_AndNotWithA500()
    {
        var response = await PostAsync("/api/labels/guarded", new { label = "   " });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"a guard that returns is a refusal, not a fault. Response: {(int)response.StatusCode} {text}");

        text.Should().Contain("label-required",
            "the error is the one passed to the Check, and it names the rule");

        text.Should().NotContain("validation.",
            "and it is not the pipeline: that operation declares no rule on Label");
    }

    /// <summary>The control: with a label the guard passes and the body answers.</summary>
    [Fact]
    public async Task AValueThatPassesTheGuard_ReachesTheAnswer()
    {
        var response = await PostAsync("/api/labels/guarded", new { label = "a-label" });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Response: {(int)response.StatusCode} {text}");

        text.Should().Contain("a-label",
            "without this, «refused» would be satisfied by a guard that always refuses");
    }
}
