using Pragmatic.Actions.Mutation;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The refusal of an <c>[Invariant]</c> that declares a code, a member and parameters (#739) reports
///     them: the code in place of the shared one, the member as <c>member</c> and as the key of
///     <c>errors</c>, the sentence with the values in it.
/// </summary>
public class AnInvariantRefusalCarriesWhatTheRuleDeclaresTests
{
    private static InvariantViolationError Declared() => new("StaysWithinTheLimit", "The total {Total} is over {Limit}", "error.order_over_limit")
    {
        DeclaredCode = "ORDER_OVER_LIMIT",
        Member = "Total",
        Parameters = new Dictionary<string, object> { ["Total"] = 120.5m, ["Limit"] = 100 },
    };

    [Fact]
    public void ADeclaredCode_IsTheCode()
        => Declared().Code.Should().Be("ORDER_OVER_LIMIT");

    [Fact]
    public void TheParameters_FillTheSentence_InTheInvariantCulture()
        => Declared().Title.Should().Be("The total 120.5 is over 100");

    [Fact]
    public void TheMember_IsReported_AndKeysTheErrors()
    {
        var extensions = new Dictionary<string, object?>();

        Declared().WriteExtensions(extensions);

        extensions["member"].Should().Be("total");
        var errors = (Dictionary<string, string[]>)extensions["errors"]!;
        errors["total"].Should().Equal("error.order_over_limit");
    }

    /// <summary>Control — a rule that declares none of them writes neither, and keeps the shared code.</summary>
    [Fact]
    public void ARuleThatDeclaresNothing_WritesNoMember()
    {
        var error = new InvariantViolationError("HasLines", "An order has at least one line");
        var extensions = new Dictionary<string, object?>();

        error.WriteExtensions(extensions);

        error.Code.Should().Be(InvariantViolationError.DefaultCode);
        extensions.Should().NotContainKey("member");
        extensions.Should().NotContainKey("errors");
    }
}
