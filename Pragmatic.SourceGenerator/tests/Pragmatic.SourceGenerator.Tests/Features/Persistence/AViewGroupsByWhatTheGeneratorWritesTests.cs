using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A <c>[QueryView]</c> groups by the key it declares — including one a relation writes, one reached
///     through a navigation a relation writes, and a <c>[Projectable]</c> member — once each.
/// </summary>
/// <remarks>
///     <para>
///         Found by the absence report of the time-off example. The view declared four keys and
///         the generated <c>Build</c> grouped by one, twice: <c>new { StartMonth = e.StartMonth,
///         StartMonth = e.StartMonth }</c>. A foreign key and a navigation that <c>[Relation.*]</c>
///         produces are not members of the entity while the transform runs, so they were skipped "gracefully"
///         — a report per team that adds up every team, and nothing said so.
///     </para>
///     <para>
///         ⚠️ The only view in the examples grouped by a column its entity declares, and so did every
///         generator test. Each of the three defects needs a member some other declaration produces.
///     </para>
/// </remarks>
public class AViewGroupsByWhatTheGeneratorWritesTests
{
    private const string Common = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        public enum Tier { Basic, Gold }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Customer : IEntity
        {
            public Tier Tier { get; private set; }
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
        public partial class Order : IEntity
        {
            public DateOnly PlacedOn { get; private set; }
            public decimal Amount { get; private set; }
            public string Status { get; private set; } = "";

            [Projectable]
            public int PlacedMonth => PlacedOn.Month;
        }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }

        """;

    private const string SalesLine = """

        [QueryView<Order>]
        [GroupBy<Order>(Properties = "CustomerId,PlacedMonth")]
        [GroupBy<Customer>(Properties = "Tier", Via = "Customer")]
        public partial class SalesLine
        {
            public Guid CustomerId { get; init; }
            public int PlacedMonth { get; init; }
            public Tier Tier { get; init; }

            [Sum<Order>(Expression = "Amount")]
            public decimal Total { get; init; }

            [Count<Order>]
            public int Orders { get; init; }
        }
        """;

    /// <summary>A foreign key a relation writes is a key.</summary>
    [Fact]
    public void AForeignKeyTheRelationWrites_IsInTheGroupKey()
    {
        var view = TheView(SalesLine, "SalesLine");

        GroupKey(view).Should().Contain("CustomerId = e.CustomerId",
            "the relation writes the column, so the view can group by it");
        view.Should().Contain("CustomerId = g.Key.CustomerId");
    }

    /// <summary>A key reached through a navigation a relation writes is a key.</summary>
    [Fact]
    public void AKeyReachedThroughAGeneratedNavigation_IsInTheGroupKey()
    {
        var view = TheView(SalesLine, "SalesLine");

        GroupKey(view).Should().Contain("Customer_Tier = e.Customer.Tier",
            "Via names a navigation the relation writes: the path is walked through it");
        view.Should().Contain("Tier = g.Key.Customer_Tier");
    }

    /// <summary>
    ///     A key named by a view property and by <c>[GroupBy]</c> is emitted once.
    /// </summary>
    /// <remarks>
    ///     A view property named like an entity property groups by it, and the class-level attribute
    ///     named it again: two members of one name in an anonymous type are <c>CS0833</c>, in a file the
    ///     author cannot edit.
    /// </remarks>
    [Fact]
    public void AKeyNamedTwice_IsEmittedOnce()
    {
        var view = TheView(SalesLine, "SalesLine");

        Regex.Matches(GroupKey(view), @"\bPlacedMonth\s*=").Count.Should().Be(1);
        Regex.Matches(GroupKey(view), @"\bCustomerId\s*=").Count.Should().Be(1);
    }

    /// <summary>A <c>[Projectable]</c> key is grouped by its body, which the database can compute.</summary>
    /// <remarks>
    ///     The getter is no column: EF Core cannot translate <c>e.PlacedMonth</c> inside a
    ///     <c>GroupBy</c>. The body is what a projection already inlines.
    /// </remarks>
    [Fact]
    public void AProjectableKey_IsItsBody()
    {
        var key = GroupKey(TheView(SalesLine, "SalesLine"));

        key.Should().Contain("PlacedMonth = e.PlacedOn.Month");
        key.Should().NotContain("e.PlacedMonth");
    }

    /// <summary>And the view the generator writes compiles.</summary>
    [Fact]
    public void TheGeneratedView_Compiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Common + SalesLine, file => file.Contains("SalesLine.QueryView", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    /// <summary>A view property named like a generated key groups by it, as a declared one does.</summary>
    [Fact]
    public void AViewPropertyNamedLikeAGeneratedKey_GroupsByIt()
    {
        var view = TheView("""

            [QueryView<Order>]
            public partial class OrdersByCustomer
            {
                public Guid CustomerId { get; init; }

                [Count<Order>]
                public int Orders { get; init; }
            }
            """, "OrdersByCustomer");

        GroupKey(view).Should().Contain("CustomerId = e.CustomerId");
    }

    /// <summary>A key that names nothing is reported, instead of being dropped.</summary>
    [Fact]
    public void AKeyThatNamesNothing_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            [QueryView<Order>]
            [GroupBy<Order>(Properties = "CustomrId")]
            public partial class Misspelt
            {
                [Count<Order>]
                public int Orders { get; init; }
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0732" && d.GetMessage().Contains("CustomrId"),
            "a key the view cannot build would make it add up across it");
    }

    /// <summary>A <c>Via</c> that names nothing is reported, instead of dropping the whole attribute.</summary>
    [Fact]
    public void AViaThatNamesNothing_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Common + """

            [QueryView<Order>]
            [GroupBy<Customer>(Properties = "Tier", Via = "Buyer")]
            public partial class WrongPath
            {
                public Tier Tier { get; init; }

                [Count<Order>]
                public int Orders { get; init; }
            }
            """);

        diagnostics.Should().Contain(d => d.Id == "PRAG0732" && d.GetMessage().Contains("Buyer"));
    }

    /// <summary>
    ///     The control: a declared scalar key is grouped as it always was, and nothing is reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "the key is reported" is satisfied by reporting every key, and "the key is in
    ///     the group" by a group that names everything.
    /// </remarks>
    [Fact]
    public void ADeclaredScalarKey_IsUnchanged_AndNothingIsReported()
    {
        const string byStatus = """

            [QueryView<Order>]
            [GroupBy<Order>(Properties = "Status")]
            public partial class OrdersByStatus
            {
                [From<Order>]
                public string Status { get; init; } = "";

                [Count<Order>]
                public int Orders { get; init; }
            }
            """;

        var (sources, diagnostics) = TraitCompilationHarness.Generate(Common + SalesLine + byStatus);

        diagnostics.Should().NotContain(d => d.Id == "PRAG0732");
        var key = GroupKey(Generated(sources, "OrdersByStatus"));
        key.Should().Contain("Status = e.Status");
        Regex.Matches(key, @"\b\w+ = ").Count.Should().Be(1, "one key declared, one key grouped");
    }

    private static string TheView(string view, string name)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Common + view);
        return Generated(sources, name);
    }

    private static string Generated(Dictionary<string, string> sources, string name)
    {
        var view = sources.FirstOrDefault(s => s.Key.Contains($".{name}.QueryView")).Value;
        view.Should().NotBeNull($"the view {name} is generated at all — otherwise every assertion is about nothing");
        return view!;
    }

    /// <summary>The anonymous key of <c>.GroupBy(e =&gt; new { … })</c>, and nothing after it.</summary>
    private static string GroupKey(string view)
    {
        var start = view.IndexOf(".GroupBy(", StringComparison.Ordinal);
        var end = view.IndexOf(".Select(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the view groups at all");
        return view[start..end];
    }
}
