using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[QueryStrategy]</c> on a mutation is declared and read by nobody, and the generator says so.
/// </summary>
/// <remarks>
///     <para>
///         The attribute targets a class, so the compiler accepts it anywhere. On a <c>[Query]</c> it is
///         honoured — the handler builds the queryable it names. On a mutation nothing reads it, and
///         nothing can: a write loads its row <b>tracked</b>, because it is about to be changed, and the
///         filters a write lifts are declared with <c>[FilterMode]</c> / <c>[WithoutFilter&lt;T&gt;]</c>.
///     </para>
///     <para>
///         ⚠️ So without a diagnostic the declaration is silence: a mutation carrying
///         <c>[QueryStrategy(Strategy = QueryStrategy.Raw)]</c> loads tracked and filtered exactly as if
///         the attribute were absent, and the build says nothing. <c>PRAG0703</c> — «this option is
///         declared and has no effect» — is the diagnostic for precisely this. Its other reports come
///         from <c>QueryFeature</c>, which never sees a mutation, so the mutation pipeline needs its own
///         report site for an attribute it does not read.
///     </para>
/// </remarks>
public class AStrategyAWriteCannotHonourTests
{
    private static string Source(string attributes) => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;

        namespace Sales
        {
            [Entity]
            public partial class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Reference { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            {{attributes}}
            public partial class UpdateOrderMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }
                public string Reference { get; init; } = "";
            }
        }
        """;

    private static SourceGenRunResult Run(string attributes)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(attributes), [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Query.QueryStrategyAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        ]);

    /// <summary>The declaration is reported, on the mutation that carries it.</summary>
    [Fact]
    public void AMutationThatDeclaresAStrategy_IsReported()
    {
        var diagnostics = GeneratorTestHelper
            .GetDiagnosticsById(Run("[QueryStrategy(Strategy = QueryStrategy.Raw)]"), "PRAG0703")
            .ToList();

        diagnostics.Should().ContainSingle("a write cannot honour a read strategy, and silence is the defect");
        var message = diagnostics[0].GetMessage();
        message.Should().Contain("UpdateOrderMutation")
            .And.Contain("[QueryStrategy]");
        message.Should().Contain("[FilterMode]").And.Contain("[WithoutFilter",
            "the message has to name what a write uses instead — otherwise it says «wrong» and not «use this»");
    }

    /// <summary>The bare form counts too: it declares the default, which is still not read here.</summary>
    [Fact]
    public void TheBareForm_IsReportedAsWell()
        => GeneratorTestHelper.GetDiagnosticsById(Run("[QueryStrategy]"), "PRAG0703")
            .Should().ContainSingle("[QueryStrategy] with no argument declares Entity, and nothing reads that either");

    /// <summary>The control: a mutation that declares no strategy is silent.</summary>
    /// <remarks>
    ///     Without it, "the mutation is reported" would be satisfied by reporting every mutation — which
    ///     would make PRAG0703 noise on the whole write side of an application.
    /// </remarks>
    [Fact]
    public void AMutationWithoutIt_IsSilent()
        => GeneratorTestHelper.GetDiagnosticsById(Run(""), "PRAG0703").Should().BeEmpty();

    /// <summary>
    ///     The second control: the write is generated exactly as before. The diagnostic is a guard, not
    ///     a refusal.
    /// </summary>
    [Fact]
    public void TheMutationIsStillGenerated()
    {
        var invoker = GeneratorTestHelper.GetGeneratedSource(
            Run("[QueryStrategy(Strategy = QueryStrategy.Raw)]"), "UpdateOrderMutation.MutationInvoker");

        invoker.Should().NotBeNull("a declaration that has no effect does not stop the operation");
    }
}
