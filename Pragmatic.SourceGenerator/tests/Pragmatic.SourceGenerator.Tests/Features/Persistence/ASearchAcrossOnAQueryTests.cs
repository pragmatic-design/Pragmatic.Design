using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[SearchAcross]</c> on a <c>[Query]</c>: one value searched in several columns, and — with
///     <c>IgnoreCase</c> — whatever its case.
/// </summary>
/// <remarks>
///     On a query the attribute was read by nobody, and said so (PRAG0703): a search box over a name,
///     an email and a number could only be a grid filter. Where it was read, it compared as written, and
///     on PostgreSQL <c>Contains</c> is case-sensitive.
/// </remarks>
public class ASearchAcrossOnAQueryTests
{
    private const string Model = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Reference { get; private set; } = "";
            public string Customer { get; private set; } = "";
            public string? Notes { get; private set; }
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        """;

    private static string Query(string attribute) => $$"""

        [Query<Order, Order>]
        public partial class FindOrders
        {
            {{attribute}}
            public string? Search { get; init; }
        }
        """;

    private const string CaseInsensitive =
        """[SearchAcross(nameof(Order.Reference), nameof(Order.Customer), nameof(Order.Notes), IgnoreCase = true)]""";

    private const string AsWritten =
        """[SearchAcross(nameof(Order.Reference), nameof(Order.Customer), nameof(Order.Notes))]""";

    /// <summary>The value is searched in every column, in the query and in its specification.</summary>
    [Fact]
    public void TheValue_IsSearchedInEveryColumn_InBothMembers()
    {
        var query = TheQuery(Model + Query(AsWritten));

        foreach (var column in new[] { "Reference", "Customer", "Notes" })
            Regex.Matches(query, $@"e\.{column}\.Contains\(").Count.Should().Be(2,
                $"{column} is searched by Apply and by ToSpecification");
        query.Should().Contain("||", "one value against several columns is an OR");
        query.Should().NotContain("e.Search", "Search is the query's own property; the entity has no such column");
    }

    /// <summary>With <c>IgnoreCase</c>, both sides are lowered, and a null column is not dereferenced.</summary>
    [Fact]
    public void IgnoreCase_LowersBothSides()
    {
        var query = TheQuery(Model + Query(CaseInsensitive));

        query.Should().Contain("e.Reference.ToLower().Contains(this.Search!.ToLower())");
        query.Should().Contain("e.Notes != null && e.Notes.ToLower().Contains(");
    }

    /// <summary>It is not reported as inert — it is not.</summary>
    [Fact]
    public void ItIsNotReportedAsInert()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Query(CaseInsensitive));

        diagnostics.Should().NotContain(d => d.Id == "PRAG0703");
    }

    /// <summary>And the query compiles.</summary>
    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model + Query(CaseInsensitive), file => file.Contains("FindOrders.Query", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>The grid filter honours <c>IgnoreCase</c> too: on the query alone it would be half an option.</summary>
    [Fact]
    public void TheGridFilter_HonoursIgnoreCase()
    {
        var grid = TheGrid(Model + """

            [GridFilter<Order>]
            public partial class OrderGrid
            {
                [SearchAcross(nameof(Order.Reference), nameof(Order.Customer), IgnoreCase = true)]
                public string? Search { get; set; }
            }
            """);

        grid.Should().Contain("e.Reference.ToLower().Contains(");
        grid.Should().Contain("e.Customer.ToLower().Contains(");
    }

    /// <summary>The control: a grid filter without it is what it was.</summary>
    [Fact]
    public void AGridFilterWithoutIt_IsUnchanged()
    {
        var grid = TheGrid(Model + """

            [GridFilter<Order>]
            public partial class OrderGrid
            {
                [SearchAcross(nameof(Order.Reference), nameof(Order.Customer))]
                public string? Search { get; set; }
            }
            """);

        grid.Should().Contain("e.Reference.Contains(");
        grid.Should().NotContain("ToLower()");
    }

    private static string TheQuery(string source) => Generated(source, "FindOrders.Query.g.cs");

    private static string TheGrid(string source) => Generated(source, "OrderGrid.");

    private static string Generated(string source, string hintPart)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var generated = sources.FirstOrDefault(s => s.Key.Contains(hintPart) && s.Value.Contains("Search")).Value;
        generated.Should().NotBeNull($"{hintPart} is generated at all — otherwise every assertion is about nothing");
        return generated!;
    }
}
