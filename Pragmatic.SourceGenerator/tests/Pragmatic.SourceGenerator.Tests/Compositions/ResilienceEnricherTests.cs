using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Compositions.Enrichers;
using Pragmatic.SourceGenerator.Tests.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Compositions;

/// <summary>
///     A blank policy name must not flow verbatim into the generated invoker: the invoker would ask the
///     registry for a policy that no configuration can register — a runtime failure on first call, with
///     nothing said at compile time.
/// </summary>
public class ResilienceEnricherTests
{
    private const string Source = """
        namespace Pragmatic.Resilience.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ResiliencePolicyAttribute : System.Attribute
            {
                public ResiliencePolicyAttribute(string policyName) { PolicyName = policyName; }
                public string PolicyName { get; }
            }
        }

        namespace MyApp
        {
            using Pragmatic.Resilience.Attributes;

            [ResiliencePolicy("payment-provider")]
            public sealed class ChargeCard { }

            [ResiliencePolicy("")]
            public sealed class EmptyPolicy { }

            [ResiliencePolicy("   ")]
            public sealed class WhitespacePolicy { }

            public sealed class NoPolicy { }
        }
        """;

    [Fact]
    public void Enrich_NamedPolicy_ProducesTheContribution()
    {
        var (contribution, blank) = ResilienceEnricher.Enrich(
            SymbolCompilationHelper.GetType(Source, "MyApp.ChargeCard"));

        blank.Should().BeFalse();
        contribution.Should().NotBeNull();
        contribution!.PolicyName.Should().Be("payment-provider");
    }

    [Theory]
    [InlineData("MyApp.EmptyPolicy")]
    [InlineData("MyApp.WhitespacePolicy")]
    public void Enrich_BlankPolicyName_ContributesNothingAndFlagsIt(string typeName)
    {
        var (contribution, blank) = ResilienceEnricher.Enrich(
            SymbolCompilationHelper.GetType(Source, typeName));

        contribution.Should().BeNull("an unusable policy name must not reach the invoker");
        blank.Should().BeTrue("the caller reports PRAG0420 rather than failing at runtime");
    }

    [Fact]
    public void Enrich_NoAttribute_ContributesNothing()
    {
        var (contribution, blank) = ResilienceEnricher.Enrich(
            SymbolCompilationHelper.GetType(Source, "MyApp.NoPolicy"));

        contribution.Should().BeNull();
        blank.Should().BeFalse();
    }
}
