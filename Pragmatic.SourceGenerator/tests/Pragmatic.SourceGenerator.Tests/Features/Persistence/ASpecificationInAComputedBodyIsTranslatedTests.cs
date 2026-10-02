using System;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[Projectable]</c> or <c>[ComputedFilter]</c> body filters its children with a named
///     specification, and what reaches the query is the specification's expression.
/// </summary>
/// <remarks>
///     <para>
///         <c>Lines.Where(LineSpecs.Shipped)</c> binds to the <c>IEnumerable</c> overload of
///         <c>SpecificationExtensions</c>, which runs the compiled delegate: right for the getter, and a
///         call EF Core cannot translate once the body is copied into <c>Expr</c>. So the rule was written
///         inline in every computed member, beside the specification that already said it.
///     </para>
///     <para>
///         The rewrite writes <c>Queryable.Where(Queryable.AsQueryable(e.Lines), (spec).ToExpression())</c>.
///         EF Core evaluates <c>ToExpression()</c> before translating — it does not read the row — and
///         inlines the lambda it returns: composed, and with a value from outside the row as a parameter.
///         An argument that reads the row cannot be evaluated first; EF Core throws at the first query, so
///         the generator refuses it (PRAG0735).
///     </para>
/// </remarks>
public class ASpecificationInAComputedBodyIsTranslatedTests
{
    private const string Header = """
        using System;
        using System.Linq;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Specification;

        namespace Shop.Entities
        {
            public sealed class ShopBoundary { }

            public enum LineState { Open, Shipped }

            public static class LineSpecs
            {
                public static Specification<Line> Shipped => Spec<Line>.Where(l => l.State == LineState.Shipped);

                public static Specification<Line> Heavy => Spec<Line>.Where(l => l.Amount >= 100);

                public static Specification<Line> AtLeast(decimal amount) => Spec<Line>.Where(l => l.Amount >= amount);
            }

            [Entity]
            [BelongsTo<ShopBoundary>]
            public partial class Line : IEntity
            {
                public decimal Amount { get; private set; }

                public LineState State { get; private set; }
            }

            [PragmaticDbContext("Shop")]
            public partial class ShopDbContext { }

        """;

    private const string Source = Header + """
            [Entity]
            [BelongsTo<ShopBoundary>]
            [Relation.OneToMany<Line>.WithNavigation("Lines", Inverse = "Order")]
            public partial class Order : IEntity
            {
                [Projectable]
                public decimal Shipped => Lines.Where(LineSpecs.Shipped).Sum(l => l.Amount);

                [ComputedFilter]
                public bool HasHeavyOrUnshippedLines => Lines.Any(LineSpecs.Heavy | !LineSpecs.Shipped);

                [ComputedFilter]
                public bool HasALineOfAtLeast(decimal amount) => Lines.Any(LineSpecs.AtLeast(amount));
            }
        }
        """;

    /// <summary>The argument reads the row — a member of the entity, and a lambda parameter of the body.</summary>
    private const string RowSource = Header + """
            [Entity]
            [BelongsTo<ShopBoundary>]
            [Relation.OneToMany<Line>.WithNavigation("Lines", Inverse = "Order")]
            public partial class Order : IEntity
            {
                public decimal Threshold { get; private set; }

                [Projectable]
                public int AboveThreshold => Lines.Count(LineSpecs.AtLeast(Threshold));

                [ComputedFilter]
                public bool HasADuplicate => Lines.Any(l => Lines.Count(LineSpecs.AtLeast(l.Amount)) > 1);
            }
        }
        """;

    [Fact]
    public void AProjectableMember_FiltersWithTheSpecificationsExpression()
    {
        Generated(Source, "Order.Projectable").Should().Contain(
            "global::System.Linq.Queryable.Where(global::System.Linq.Queryable.AsQueryable(e.Lines), "
            + "(global::Shop.Entities.LineSpecs.Shipped).ToExpression()).Sum(l => l.Amount)");
    }

    [Fact]
    public void AComposedSpecification_IsTranslatedAsComposed()
    {
        Generated(Source, "Order.ComputedFilter").Should().Contain(
            "global::System.Linq.Queryable.Any(global::System.Linq.Queryable.AsQueryable(e.Lines), "
            + "(global::Shop.Entities.LineSpecs.Heavy | !global::Shop.Entities.LineSpecs.Shipped).ToExpression())");
    }

    [Fact]
    public void AFilterMethodsParameter_ReachesTheSpecification()
    {
        Generated(Source, "Order.ComputedFilter").Should().Contain(
            "global::System.Linq.Queryable.Any(global::System.Linq.Queryable.AsQueryable(e.Lines), "
            + "(global::Shop.Entities.LineSpecs.AtLeast(amount)).ToExpression())");
    }

    /// <summary>And what the generator writes compiles.</summary>
    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Source, file => file.Contains("Order.ComputedFilter", StringComparison.Ordinal)
                            || file.Contains("Order.Projectable", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    [Fact]
    public void AnArgumentThatReadsTheRow_IsRefused()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(RowSource);

        diagnostics.Where(d => d.Id == "PRAG0735").Select(d => d.GetMessage())
            .Should().HaveCount(2)
            .And.Contain(m => m.Contains("LineSpecs.AtLeast(Threshold)", StringComparison.Ordinal))
            .And.Contain(m => m.Contains("LineSpecs.AtLeast(l.Amount)", StringComparison.Ordinal));
    }

    /// <summary>The control: a value from outside the row — a filter method's parameter — is not refused.</summary>
    [Fact]
    public void AValueFromOutsideTheRow_IsNotRefused()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Source);

        diagnostics.Where(d => d.Id == "PRAG0735").Should().BeEmpty();
    }

    private static string Generated(string source, string hintPart)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var generated = sources.FirstOrDefault(s => s.Key.Contains(hintPart)).Value;
        generated.Should().NotBeNull($"{hintPart} is generated at all — otherwise every assertion is about nothing");
        return generated!;
    }
}
