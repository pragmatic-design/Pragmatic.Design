using System;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The <c>Expression</c> of <c>[Sum]</c>, <c>[Avg]</c>, <c>[Min]</c> and <c>[Max]</c> is written over
///     the row: every member it names is read from <c>x</c>, and a <c>[Projectable]</c> one is its body.
/// </summary>
/// <remarks>
///     The template pasted the expression after <c>x.</c>, which is right for one member and wrong for
///     anything else. The attribute's own example — <c>"Quantity * UnitPrice"</c> — became
///     <c>x.Quantity * UnitPrice</c>, a <c>CS0103</c> in a file the author cannot edit; and the
///     documentation's sum over a <c>[Projectable]</c> member read a getter EF Core cannot translate.
/// </remarks>
public class AnAggregateIsWrittenOverTheRowTests
{
    private const string Common = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class OrderLine : IEntity
        {
            public string Status { get; private set; } = "";
            public int Quantity { get; private set; }
            public decimal UnitPrice { get; private set; }

            [Projectable]
            public decimal AmountWithTax => Quantity * UnitPrice * 1.22m;
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        [QueryView<OrderLine>]
        [GroupBy<OrderLine>(Properties = "Status")]
        public partial class LinesByStatus
        {
            [From<OrderLine>]
            public string Status { get; init; } = "";

            [Sum<OrderLine>(Expression = "Quantity * UnitPrice")]
            public decimal Net { get; init; }

            [Sum<OrderLine>(Expression = "AmountWithTax")]
            public decimal Gross { get; init; }

            [Max<OrderLine>(Expression = "UnitPrice")]
            public decimal HighestPrice { get; init; }
        }
        """;

    /// <summary>Every member an arithmetic expression names is read from the row.</summary>
    [Fact]
    public void AnArithmeticExpression_ReadsEveryMemberFromTheRow()
    {
        TheView().Should().Contain("Net = g.Sum(x => x.Quantity * x.UnitPrice)");
    }

    /// <summary>A <c>[Projectable]</c> member is summed as its body, which the database can compute.</summary>
    [Fact]
    public void AProjectableMember_IsAggregatedAsItsBody()
    {
        var view = TheView();

        view.Should().Contain("Gross = g.Sum(x => (x.Quantity * x.UnitPrice * 1.22m))");
        view.Should().NotContain("x.AmountWithTax");
    }

    /// <summary>
    ///     The control: a single member is written as it always was.
    /// </summary>
    /// <remarks>
    ///     Without it, "the expression is rewritten" would be satisfied by a rewrite that also mangled
    ///     the one shape every existing view uses.
    /// </remarks>
    [Fact]
    public void ASingleMember_IsUnchanged()
    {
        TheView().Should().Contain("HighestPrice = g.Max(x => x.UnitPrice)");
    }

    /// <summary>And the view the generator writes compiles.</summary>
    [Fact]
    public void TheGeneratedView_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Common, file => file.Contains("LinesByStatus.QueryView", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string TheView()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common);
        var view = sources.FirstOrDefault(s => s.Key.Contains(".LinesByStatus.QueryView")).Value;
        view.Should().NotBeNull("the view is generated at all — otherwise every assertion is about nothing");
        return view!;
    }
}
