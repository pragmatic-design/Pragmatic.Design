using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resilience;

/// <summary>
///     The module's generated registration declares the <c>[ResiliencePolicy]</c> name each action asks
///     for, so the host can check at startup that it is defined.
/// </summary>
public sealed class AnActionsPolicyNameIsDeclaredTests
{
    private const string Usings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Resilience.Attributes;
        using Pragmatic.Result;

        """;

    private static string Action(string attribute) => Usings + $$"""
        namespace TestApp.Payments;

        [DomainAction]
        {{attribute}}
        public partial class ChargeCard : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("ok"));
        }
        """;

    [Fact]
    public void AnActionWithAPolicy_IsDeclaredInTheRegistration()
    {
        var registration = Registration(Action("[ResiliencePolicy(\"payments\")]"));

        registration.Should().Contain(
            "services.AddSingleton(new global::Pragmatic.Resilience.DeclaredResiliencePolicy(\"payments\", \"TestApp.Payments.ChargeCard\"));");
    }

    /// <summary>The control: an action without a policy declares nothing.</summary>
    [Fact]
    public void AnActionWithoutAPolicy_DeclaresNothing()
        => Registration(Action("")).Should().NotContain("DeclaredResiliencePolicy");

    private static string Registration(string source)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GeneratorTestHelper.FromType<global::Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.DomainActionAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>());

        // That it compiles is TheHostChecksThePolicyNamesItWasGivenTests' to say: it builds module and
        // host against the whole reference set, which this minimal one is not.
        return GeneratorTestHelper.GetGeneratedSource(result, "_Infra.Actions.Registration")
               ?? throw new InvalidOperationException("No action registration was generated.");
    }
}
