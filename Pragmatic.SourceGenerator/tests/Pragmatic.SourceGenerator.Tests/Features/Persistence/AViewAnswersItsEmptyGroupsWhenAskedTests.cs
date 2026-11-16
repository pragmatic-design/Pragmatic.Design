using System;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[GroupBy(IncludeEmpty = true)]</c> (#766): a view grouped by one enum key answers a row for every member, and what it cannot answer
///     for an empty group is refused at compile time.
/// </summary>
/// <remarks>
///     The effect, a row per status on the three engines, is measured in the Showcase (<c>AnAggregateReadIsAQueryTests</c>). These cases
///     measure the shape the engines rely on and the three refusals, each with its control.
/// </remarks>
public class AViewAnswersItsEmptyGroupsWhenAskedTests
{
    private const string Common = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        public enum Stage { Draft, Placed, Shipped }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Stage Stage { get; private set; }
            public string Region { get; private set; } = "";
            public decimal Amount { get; private set; }
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        """;

    private const string ByStage = """

        [QueryView<Order>]
        [GroupBy<Order>(Properties = "Stage", IncludeEmpty = true)]
        public partial class OrdersByStage
        {
            [From<Order>]
            public Stage Stage { get; init; }

            [Count<Order>]
            public int Orders { get; init; }

            [Sum<Order>(Expression = "Amount")]
            public decimal Total { get; init; }

            [Max<Order>(Expression = "Amount")]
            public decimal? Largest { get; init; }
        }
        """;

    [Fact]
    public void AnEnumKey_GeneratesWithEmptyGroups_OverEveryMember()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Common + ByStage);

        diagnostics.Should().NotContain(d => d.Id is "PRAG0748" or "PRAG0749" or "PRAG0750");
        var view = sources.Single(s => s.Key.Contains(".OrdersByStage.QueryView")).Value;
        view.Should().Contain("WithEmptyGroups");
        view.Should().Contain("global::TestApp.Stage.Draft, global::TestApp.Stage.Placed, global::TestApp.Stage.Shipped",
            "the values are the enum's members, written at compile time");
        view.Should().Contain("Orders = 0,").And.Contain("Total = 0,").And.Contain("Largest = null,");
    }

    [Fact]
    public void TheGeneratedViewAndQuery_Compile()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(Common + ByStage + """

            [Query<Order, OrdersByStage>]
            public partial class OrdersByStageQuery { }
            """, file => file.Contains("OrdersByStage", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>The control: without the opt-in nothing is added, and nothing is refused.</summary>
    [Fact]
    public void WithoutTheOptIn_NoWithEmptyGroups_AndNothingIsRefused()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(Common + ByStage.Replace(", IncludeEmpty = true", ""));

        diagnostics.Should().NotContain(d => d.Id is "PRAG0748" or "PRAG0749" or "PRAG0750");
        sources.Single(s => s.Key.Contains(".OrdersByStage.QueryView")).Value.Should().NotContain("WithEmptyGroups");
    }

    [Fact]
    public void ACompositeKey_IsRefused()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            [QueryView<Order>]
            [GroupBy<Order>(Properties = "Stage,Region", IncludeEmpty = true)]
            public partial class OrdersByStageAndRegion
            {
                public Stage Stage { get; init; }
                public string Region { get; init; } = "";

                [Count<Order>]
                public int Orders { get; init; }
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0748" && d.GetMessage().Contains("'Region'"));
    }

    [Fact]
    public void AKeyThatIsNotAnEnum_IsRefused()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            [QueryView<Order>]
            [GroupBy<Order>(Properties = "Region", IncludeEmpty = true)]
            public partial class OrdersByRegion
            {
                public string Region { get; init; } = "";

                [Count<Order>]
                public int Orders { get; init; }
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0749" && d.GetMessage().Contains("'Region'"));
    }

    [Fact]
    public void ANonNullableMax_IsRefused()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + ByStage.Replace("decimal? Largest", "decimal Largest"));

        diagnostics.Should().Contain(d => d.Id == "PRAG0750" && d.GetMessage().Contains("'Largest'"),
            "an empty group has no largest order: a non-nullable decimal would answer a 0 nobody can tell from a real one");
        diagnostics.Should().NotContain(d => d.Id == "PRAG0750" && d.GetMessage().Contains("'Total'"),
            "a sum of no rows is 0, which is its value and not a default");
    }
}
