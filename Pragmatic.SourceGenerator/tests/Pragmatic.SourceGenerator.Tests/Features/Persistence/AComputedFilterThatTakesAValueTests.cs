using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[ComputedFilter]</c> on a method: a rule over the row and a value the caller brings — "open on
///     this day" — generated as a specification and an extension that take the value.
/// </summary>
/// <remarks>
///     A property can only say what the row says. The one way to write "today" in it was
///     <c>DateTime.UtcNow</c>, which EF sends to the database as <c>now()</c>: the database's clock, not
///     the application's, and out of reach of a clock a test or a host injects.
/// </remarks>
public class AComputedFilterThatTakesAValueTests
{
    private const string Common = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        """;

    private const string Order = """

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public DateOnly PlacedOn { get; private set; }
            public DateOnly? ShippedOn { get; private set; }

            [ComputedFilter]
            public bool IsOpenOn(DateOnly day) => PlacedOn <= day && (ShippedOn == null || ShippedOn > day);

            [ComputedFilter]
            public bool IsShipped => ShippedOn != null;
        }
        """;

    /// <summary>The method becomes a specification that takes the value.</summary>
    [Fact]
    public void AMethod_IsASpecificationThatTakesTheValue()
    {
        var filters = TheFilters(Common + Order);

        filters.Should().Contain("IsOpenOnSpec(global::System.DateOnly day)");
        filters.Should().Contain("e.PlacedOn <= day",
            "the entity's members are read from the row, the parameter stays the parameter");
    }

    /// <summary>And an extension on the query that takes it too.</summary>
    [Fact]
    public void AMethod_IsAnExtensionThatTakesTheValue()
    {
        TheFilters(Common + Order).Should().Contain(
            "WhereOpenOn(this global::System.Linq.IQueryable<global::TestApp.Order> query, global::System.DateOnly day)");
    }

    /// <summary>
    ///     Every parameter of the extension is documented: a module that builds its documentation stops on
    ///     CS1573 as soon as one is and another is not, and the harness here builds none.
    /// </summary>
    [Fact]
    public void TheExtension_DocumentsEveryParameter()
    {
        var filters = TheFilters(Common + Order);

        filters.Should().Contain("<param name=\"query\">");
        filters.Should().Contain("<param name=\"day\">");
    }

    /// <summary>The control: a property filter is what it was — a field and an extension without arguments.</summary>
    [Fact]
    public void AProperty_IsUnchanged()
    {
        var filters = TheFilters(Common + Order);

        filters.Should().Contain("IsShippedSpec =");
        filters.Should().Contain("WhereShipped(this global::System.Linq.IQueryable<global::TestApp.Order> query)");
    }

    /// <summary>And the filters the generator writes compile.</summary>
    [Fact]
    public void TheGeneratedFilters_Compile()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Common + Order, file => file.Contains("Order.ComputedFilter", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>A shape the generator cannot write is reported, instead of generating nothing.</summary>
    [Theory]
    [InlineData("public int CountOn(DateOnly day) => 1;")]
    [InlineData("public static bool IsAnyOn(DateOnly day) => true;")]
    [InlineData("public bool IsOneOf(params int[] values) => true;")]
    [InlineData("public int Weight => 1;")]
    public void AShapeTheGeneratorCannotWrite_IsPRAG0733(string member)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + $$"""

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Parcel : IEntity
            {
                public DateOnly SentOn { get; private set; }

                [ComputedFilter]
                {{member}}
            }
            """);

        Ids(diagnostics).Should().Contain("PRAG0733");
    }

    /// <summary>The control: the shapes that are written report nothing.</summary>
    [Fact]
    public void TheShapesThatAreWritten_ReportNothing()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + Order);

        Ids(diagnostics).Should().NotContain("PRAG0733");
    }

    private static string TheFilters(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var filters = sources.FirstOrDefault(s => s.Key.Contains(".Order.ComputedFilter")).Value;
        filters.Should().NotBeNull("the filters are generated at all — otherwise every assertion is about nothing");
        return filters!;
    }

    private static IEnumerable<string> Ids(IEnumerable<Diagnostic> diagnostics) => diagnostics.Select(d => d.Id);
}
