using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[FromClock]</c> on a query property: the generated invoker fills it from the application's
///     clock, and nothing else can.
/// </summary>
/// <remarks>
///     "Who is away today" is a read whose value is the date. Taken from the caller, anyone could ask
///     for another day and call it today; written as <c>DateTime.UtcNow</c> inside a rule, it is the
///     database's clock. The mutations that decide and withdraw read the application's <c>IClock</c>, so
///     the read has to as well.
/// </remarks>
public class AQueryReadsTheClockTests
{
    private const string Model = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Specification;
        using Pragmatic.Temporal.Clock;

        namespace TestApp
        {
            [Boundary]
            public partial class SalesBoundary { }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public DateOnly PlacedOn { get; set; }
                public DateTimeOffset CreatedAt { get; set; }
                public string Reference { get; set; } = "";
            }

            [PragmaticDbContext("Sales")]
            public partial class SalesDbContext { }
        }
        """;

    private static string Query(string members) => $$"""

        namespace TestApp
        {
            [Query<Order, Order>]
            [Endpoint(HttpVerb.Get, "api/orders/placed-by-now")]
            public partial class PlacedByNowQuery
            {
                {{members}}
            }
        }
        """;

    private const string Bound = """
        [FromClock]
        public DateOnly Today { get; private set; }

        [FromClock]
        public DateTimeOffset Now { get; private set; }

        public Specification<Order> PlacedByToday => Spec<Order>.Where(o => o.PlacedOn <= Today && o.CreatedAt <= Now);

        [Filter]
        public string? Reference { get; set; }
        """;

    // =========================================================================
    // The invoker binds
    // =========================================================================

    /// <summary>A date is the clock's UTC date, and an instant is its UTC now.</summary>
    [Fact]
    public void TheInvoker_WritesTheClock()
    {
        var invoker = Invoker(Model + Query(Bound));

        invoker.Should().Contain("GetRequiredService<global::Pragmatic.Temporal.Clock.IClock>(_services)",
            "the host has a clock, and one without is a configuration error, not a default");
        invoker.Should().Contain("q.Today = __clock.UtcToday;");
        invoker.Should().Contain("q.Now = __clock.UtcNow;");
    }

    /// <summary>The control: a query without the attribute asks for no clock.</summary>
    [Fact]
    public void AQueryWithoutIt_AsksForNoClock()
    {
        Invoker(Model + Query("""
            [Filter]
            public string? Reference { get; set; }
            """)).Should().NotContain("IClock");
    }

    /// <summary>The query, the invoker and the boundary compile, with the private setter only the invoker reaches.</summary>
    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model + Query(Bound),
            static path => path.EndsWith(".QueryInvoker.g.cs") || path.EndsWith(".Query.g.cs")
                           || path.Contains("_Boundary.") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    // =========================================================================
    // It is neither a parameter nor a filter
    // =========================================================================

    /// <summary>The query filters by nothing of its own for it: a specification reads it.</summary>
    [Fact]
    public void TheQuery_AppliesNoFilterForIt()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Model + Query(Bound));

        var apply = sources.Single(pair => pair.Key.EndsWith("PlacedByNowQuery.Query.g.cs")).Value;

        apply.Should().NotContain("e.Today");
        apply.Should().NotContain("e.Now");
        apply.Should().Contain("PlacedByToday", "the specification that reads it is applied");
        Ids(diagnostics).Should().NotContain("PRAG0707", "the binding is what the value is for");
    }

    /// <summary>The route does not read it from the request.</summary>
    [Fact]
    public void TheRoute_DoesNotReadIt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + Query(Bound));

        var endpoint = sources.Single(pair => pair.Key.EndsWith("PlacedByNowQuery.Endpoint.g.cs")).Value;

        endpoint.Should().NotContain("Today");
        endpoint.Should().Contain("Reference", "the control: an ordinary input is still read");
    }

    /// <summary>And another module cannot pass it through the boundary.</summary>
    [Fact]
    public void TheBoundary_DoesNotTakeIt()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model + Query(Bound));

        var facade = sources.Where(pair => pair.Key.Contains("_Boundary.SalesBoundary.")).ToList();

        facade.Should().NotBeEmpty();
        facade.Should().Contain(pair => pair.Value.Contains("Reference = reference,"),
            "the control: an ordinary input is still a parameter of the overload");
        foreach (var (hint, source) in facade)
            source.Should().NotContain("Today", hint);
    }

    // =========================================================================
    // PRAG0734 — the binding cannot be written
    // =========================================================================

    [Theory]
    [InlineData("[FromClock] public string Today { get; private set; } = \"\";")]
    [InlineData("[FromClock] public DateOnly Today { get; set; }")]
    [InlineData("[FromClock] public DateOnly Today { get; init; }")]
    public void ABindingTheInvokerCannotWrite_IsPRAG0734(string declaration)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Query(declaration));

        Ids(diagnostics).Should().Contain("PRAG0734");
    }

    /// <summary>The control: the form is not reported.</summary>
    [Fact]
    public void TheForm_IsNotReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Model + Query(Bound));

        Ids(diagnostics).Should().NotContain("PRAG0734");
    }

    private static string Invoker(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        return sources.Single(pair => pair.Key.EndsWith("PlacedByNowQuery.QueryInvoker.g.cs")).Value;
    }

    private static IEnumerable<string> Ids(IEnumerable<Diagnostic> diagnostics) => diagnostics.Select(d => d.Id);
}
