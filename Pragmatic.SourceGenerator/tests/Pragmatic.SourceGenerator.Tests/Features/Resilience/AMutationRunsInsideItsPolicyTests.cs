using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resilience;

/// <summary>
///     <c>[ResiliencePolicy("name")]</c> on a mutation: the generated invoker runs its attempts inside the
///     named pipeline, and the registration declares the name for the startup check.
/// </summary>
/// <remarks>
///     Until then the attribute was read into the model, counted as a resilience declaration — the host
///     wired <c>AddPragmaticResilience()</c> for it — and no template asked for a pipeline: the mutation ran
///     once, with nothing saying so. What the override does at run time is
///     <c>AMutationUnderAPolicyIsRetriedTests</c>' to say, in Pragmatic.Actions.Tests.
/// </remarks>
public sealed class AMutationRunsInsideItsPolicyTests
{
    private static string Source(string attribute) => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Resilience.Attributes;

        namespace Sales
        {
            [Entity]
            public partial class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Reference { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Create)]
            {{attribute}}
            public partial class PlaceOrder : Mutation<Order>
            {
                public string Reference { get; init; } = "";
            }
        }
        """;

    private static SourceGenRunResult Run(string attribute)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(attribute), [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        ]);

    private static string Invoker(string attribute)
        => GeneratorTestHelper.GetGeneratedSource(Run(attribute), "PlaceOrder.MutationInvoker")
           ?? throw new InvalidOperationException("No mutation invoker was generated.");

    private static string Registration(string attribute)
        => GeneratorTestHelper.GetGeneratedSource(Run(attribute), "_Infra.Mutations.Registration")
           ?? throw new InvalidOperationException("No mutation registration was generated.");

    [Fact]
    public void TheInvoker_RunsItsAttemptsInsideTheNamedPipeline()
    {
        var invoker = Invoker("[ResiliencePolicy(\"orders\")]");

        invoker.Should().Contain("protected override async global::System.Threading.Tasks.Task<")
            .And.Contain("RunAttemptsAsync(");
        invoker.Should().Contain("_resiliencePipelineProvider.GetPipeline(\"orders\")");
        invoker.Should().Contain("(ctx, innerCt) => attempt(innerCt)");
        invoker.Should().Contain("OperationName = \"PlaceOrder\"");
        invoker.Should().Contain("ResilienceResultBridge.TryMapToError",
            "an exhausted retry or an open circuit answers with its error, not a raw 500");
    }

    [Fact]
    public void TheInvoker_TakesThePipelineProvider()
    {
        var invoker = Invoker("[ResiliencePolicy(\"orders\")]");

        invoker.Should().Contain("global::Pragmatic.Resilience.IResiliencePipelineProvider resiliencePipelineProvider")
            .And.Contain("_resiliencePipelineProvider = resiliencePipelineProvider;");
    }

    [Fact]
    public void TheRegistration_DeclaresThePolicyName()
        => Registration("[ResiliencePolicy(\"orders\")]").Should().Contain(
            "services.AddSingleton(new global::Pragmatic.Resilience.DeclaredResiliencePolicy(\"orders\", \"Sales.PlaceOrder\"));");

    /// <summary>The control: without the attribute the invoker runs once and asks for no pipeline.</summary>
    [Fact]
    public void WithoutAPolicy_NothingIsWrapped()
    {
        Invoker("").Should().NotContain("RunAttemptsAsync").And.NotContain("Resilience");
        Registration("").Should().NotContain("DeclaredResiliencePolicy");
    }
}
