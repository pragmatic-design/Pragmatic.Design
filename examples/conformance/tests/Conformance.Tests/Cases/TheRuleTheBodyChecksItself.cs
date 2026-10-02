using System.Net;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[NoValidation]</c>: the declared rule stays, but the body is what refuses.
/// </summary>
/// <remarks>
///     <para>
///         <c>CheckRulesByHandAction</c> carries the same <c>[MinLength(3)]</c> as
///         <c>CheckEveryRuleAction</c>, plus <c>[NoValidation]</c>. The same value — <c>"ab"</c> — produces
///         two different 422s, and the difference is all in <b>who</b> answered: over there the rule's key
///         under the property's name, here the domain rule in <c>rule</c>.
///     </para>
///     <para>
///         ⚠️ The control is not next to it, it is <c>EveryRuleIsExecuted</c>: it sends the same <c>"ab"</c>
///         to the twin without the attribute and receives <c>validation.minlength</c>. A copy of it would add
///         nothing, and the two cases run in the same suite. Without that half, «422» here would also be
///         satisfied by a <c>[NoValidation]</c> that does nothing — the pipeline would refuse anyway, and the
///         body would never know.
///     </para>
///     <para>
///         The positive case is the other half of the same question: with <c>"abc"</c> the body answers, and
///         its answer is what proves that <c>Execute</c> was reached in both branches.
///     </para>
///     <para>
///         Measured by removal: without <c>[NoValidation]</c>, <b>a single test</b> goes red, on the right
///         assertion — <c>checked-by-hand</c> no longer appears, because the pipeline refuses again before
///         <c>Execute</c> starts.
///     </para>
/// </remarks>
public class TheRuleTheBodyChecksItself(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task TheDeclaredRule_DoesNotRefuse_TheBodyDoes()
    {
        var response = await PostAsync("/api/rules/by-hand", new { atLeastThree = "ab" });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"the body refuses the same value, with an error of its own. Response: {(int)response.StatusCode} {text}");

        text.Should().Contain("checked-by-hand",
            "Execute is what answers: it is the domain rule that names the refusal");

        text.Should().NotContain("validation.minlength",
            "and not the declared rule — which [NoValidation] stopped executing, not rendering");
    }

    [Fact]
    public async Task AValueThatSatisfiesIt_ReachesTheBodyToo()
    {
        var response = await PostAsync("/api/rules/by-hand", new { atLeastThree = "abc" });
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"Response: {(int)response.StatusCode} {text}");

        text.Should().Contain("checked by hand", "the response is the one Execute built");
    }
}
