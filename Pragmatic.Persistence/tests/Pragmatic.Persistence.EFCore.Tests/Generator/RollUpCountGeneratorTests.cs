using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     <c>RollUpAggregation.Count</c>: a parent that counts its children.
/// </summary>
/// <remarks>
///     <para>
///         The enum offered two members and the transform read neither: whatever the aggregation, it
///         emitted <c>Amount = c =&gt; c.{childProperty}</c> and applied the delta as a
///         <c>decimal</c>. Counting rows therefore produced <c>CS0029</c> in the registration — the
///         child property named for a count is not a number — and <c>CS0266</c> in the parent's apply
///         method, where a <c>decimal</c> delta lands on an <c>int</c>.
///     </para>
///     <para>
///         Only <c>Sum</c> over a <c>decimal</c> was exercised anywhere, which is why a member nobody
///         had run could stay in a public enum. These cases run the other one.
///     </para>
/// </remarks>
public class RollUpCountGeneratorTests
{
    private const string CountingParent = """
        using Pragmatic.Persistence.Entity;

        namespace TestApp;

        [Entity]
        [Relation.OneToMany<Mention>]
        public partial class Term : IEntity
        {
            // The child property is ignored for Count, and the attribute says so.
            [RollUp<Mention>("", RollUpAggregation.Count)]
            public int MentionCount { get; private set; }
        }

        [Entity]
        [Relation.ManyToOne<Term>]
        public partial class Mention : IEntity
        {
            public string Where { get; private set; } = "";
        }
        """;

    /// <summary>Each child counts one: the rule reads a constant, not a property.</summary>
    [Fact]
    public void Count_RegistersARuleThatReadsOnePerChild()
    {
        var registration = GeneratorTestHelper.GetGeneratedSource(RunGenerator(CountingParent), "RollUp.Registration");

        registration.Should().NotBeNull("the parent declares a roll-up, so a rule is registered");
        registration!.Should().Contain("Amount = c => 1m",
            "counting does not read a child property — naming one is what produced the CS0029");
        registration.Should().Contain("AggregatePropertyName = \"MentionCount\"");
    }

    /// <summary>The delta reaches an <c>int</c> aggregate as an <c>int</c>.</summary>
    [Fact]
    public void Count_AppliesTheDeltaToAnIntegerAggregate()
    {
        var parent = GeneratorTestHelper.GetGeneratedSource(RunGenerator(CountingParent), "Term.RollUp");

        parent.Should().NotBeNull();
        parent!.Should().Contain("MentionCount += (int)delta",
            "the pipeline carries decimals and the aggregate is an int — the conversion is the fix "
            + "for the CS0266, and it is exact because a count moves by whole numbers");
    }

    /// <summary>
    ///     And all of it compiles, which is the assertion the other two stand on.
    /// </summary>
    /// <remarks>
    ///     Both defects in this issue were compilation errors in generated files, so a test that only
    ///     reads the text would have passed while the consumer could not build.
    /// </remarks>
    [Fact]
    public void Count_ProducesCodeThatCompiles()
    {
        var result = RunGenerator(CountingParent);

        var errors = string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result)
            .Select(d => d.GetMessage()).Take(5));

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            $"a roll-up that counts has to build — {errors}");
    }

    /// <summary>The control: summing still reads the child property it names.</summary>
    /// <remarks>
    ///     Beside the cases above so "reads a constant" cannot be satisfied by emitting <c>1m</c> for
    ///     every roll-up, which would silently turn every existing sum into a count.
    /// </remarks>
    [Fact]
    public void Sum_StillReadsTheChildProperty()
    {
        const string summingParent = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<Line>]
            public partial class Invoice : IEntity
            {
                [RollUp<Line>(nameof(Line.Amount))]
                public decimal Subtotal { get; private set; }
            }

            [Entity]
            [Relation.ManyToOne<Invoice>]
            public partial class Line : IEntity
            {
                public decimal Amount { get; private set; }
            }
            """;

        var result = RunGenerator(summingParent);

        GeneratorTestHelper.GetGeneratedSource(result, "RollUp.Registration")
            .Should().Contain("Amount = c => c.Amount");
        GeneratorTestHelper.GetGeneratedSource(result, "Invoice.RollUp")
            .Should().Contain("Subtotal += delta", "a decimal aggregate needs no conversion");
    }

    private static SourceGenRunResult RunGenerator(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>(),
        GeneratorTestHelper.TryGetAssemblyReference("Microsoft.Extensions.DependencyInjection.Abstractions")!,
        GeneratorTestHelper.TryGetAssemblyReference("Microsoft.Extensions.Logging.Abstractions")!,
        GeneratorTestHelper.TryGetAssemblyReference("System.ComponentModel.Annotations")!,
        GeneratorTestHelper.TryGetAssemblyReference("Microsoft.EntityFrameworkCore")!,
        GeneratorTestHelper.TryGetAssemblyReference("Microsoft.EntityFrameworkCore.Abstractions")!,
        GeneratorTestHelper.TryGetAssemblyReference("Microsoft.EntityFrameworkCore.Relational")!,
        GeneratorTestHelper.TryGetAssemblyReference("System.Text.Json")!,
        GeneratorTestHelper.TryGetAssemblyReference("System.ComponentModel.TypeConverter")!,
        GeneratorTestHelper.TryGetAssemblyReference("System.Linq.Queryable")!,
        GeneratorTestHelper.TryGetAssemblyReference("Pragmatic.Specification")!,
        GeneratorTestHelper.TryGetAssemblyReference("Pragmatic.Mapping")!,
        GeneratorTestHelper.TryGetAssemblyReference("Pragmatic.Mapping.EFCore")!,
        GeneratorTestHelper.TryGetAssemblyReference("Pragmatic.Ensure")!,
    ];
}
