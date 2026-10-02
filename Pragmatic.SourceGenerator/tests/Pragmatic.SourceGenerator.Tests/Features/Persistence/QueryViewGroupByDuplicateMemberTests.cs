using System.Text.RegularExpressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Regression tests for the QueryView Build template object initializer.
///     A grouped view must initialize each view member at most once. Emitting the group key from
///     <c>g.Key</c> AND again from a colliding <c>[From]</c>/aggregate property produces
///     <c>CS1912 duplicate initialization of member</c>.
/// </summary>
public class QueryViewGroupByDuplicateMemberTests
{
    /// <summary>
    ///     A column that is both a [GroupBy] key (IsProjected) and a [From] source property of the
    ///     same name must be initialized exactly once — from the group key.
    /// </summary>
    [Fact]
    public void GroupByKey_AlsoSourceProperty_InitializesMemberOnce()
    {
        var model = new QueryViewModel
        {
            TypeName = "OrdersByStatusView",
            Namespace = "MyApp.Sales.Views",
            RootEntityTypeFullName = "MyApp.Sales.Entities.Order",
            GroupByProperties =
            [
                new GroupByModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityProperty = "Status",
                    IsProjected = true
                }
            ],
            SourceProperties =
            [
                // The user also annotated [From<Order>] on the Status view property.
                new SourcePropertyModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityPropertyPath = "Status"
                }
            ],
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "OrderCount",
                    PropertyType = "int",
                    Kind = AggregateKind.Count,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e"
                }
            ]
        };

        var source = Render(model);

        // The grouping column is read from g.Key and must NOT be re-initialized from g.First().
        CountMemberInitializers(source, "Status").Should().Be(1);
        source.Should().Contain("Status = g.Key.Status");
        source.Should().NotContain("Status = g.First().Status");
        source.Should().Contain("OrderCount =");
    }

    /// <summary>
    ///     A column that is both a [GroupBy] key and an aggregate of the same name is initialized once.
    /// </summary>
    [Fact]
    public void GroupByKey_AlsoAggregate_InitializesMemberOnce()
    {
        var model = new QueryViewModel
        {
            TypeName = "RegionTotalsView",
            Namespace = "MyApp.Sales.Views",
            RootEntityTypeFullName = "MyApp.Sales.Entities.Order",
            GroupByProperties =
            [
                new GroupByModel
                {
                    PropertyName = "Region",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityProperty = "Region",
                    IsProjected = true
                }
            ],
            AggregateProperties =
            [
                // Pathological collision: an aggregate that happens to reuse the key name.
                new AggregatePropertyModel
                {
                    PropertyName = "Region",
                    PropertyType = "int",
                    Kind = AggregateKind.Count,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e"
                }
            ]
        };

        var source = Render(model);

        CountMemberInitializers(source, "Region").Should().Be(1);
    }

    /// <summary>
    ///     Sanity: distinct members are still all emitted (no over-deduplication).
    /// </summary>
    [Fact]
    public void DistinctMembers_AllEmitted()
    {
        var model = new QueryViewModel
        {
            TypeName = "SalesReportView",
            Namespace = "MyApp.Sales.Views",
            RootEntityTypeFullName = "MyApp.Sales.Entities.Order",
            GroupByProperties =
            [
                new GroupByModel
                {
                    PropertyName = "Status",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityProperty = "Status",
                    IsProjected = true
                }
            ],
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "OrderCount",
                    PropertyType = "int",
                    Kind = AggregateKind.Count,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e"
                }
            ],
            SourceProperties =
            [
                new SourcePropertyModel
                {
                    PropertyName = "Region",
                    PropertyType = "string",
                    EntityType = "MyApp.Sales.Entities.Order",
                    EntityPropertyPath = "Region"
                }
            ]
        };

        var source = Render(model);

        CountMemberInitializers(source, "Status").Should().Be(1);
        CountMemberInitializers(source, "OrderCount").Should().Be(1);
        CountMemberInitializers(source, "Region").Should().Be(1);
    }

    /// <summary>
    ///     A QueryView with aggregate properties but no [GroupBy] fails loud. Emitting dead comments
    ///     instead would leave the aggregate members at their default (0/null) — a report would silently
    ///     show zeros.
    /// </summary>
    [Fact]
    public void AggregatesWithoutGroupBy_FailLoud_InsteadOfSilentZeros()
    {
        var model = new QueryViewModel
        {
            TypeName = "OrderTotalsView",
            Namespace = "MyApp.Sales.Views",
            RootEntityTypeFullName = "MyApp.Sales.Entities.Order",
            AggregateProperties =
            [
                new AggregatePropertyModel
                {
                    PropertyName = "Total",
                    PropertyType = "decimal",
                    Kind = AggregateKind.Sum,
                    EntityType = "MyApp.Sales.Entities.Order",
                    Expression = "e.Amount"
                }
            ]
        };

        var source = Render(model);

        source.Should().Contain("throw new global::System.NotSupportedException");
        source.Should().Contain("without a [GroupBy]");
        // The old silent dead-comment aggregate must be gone.
        source.Should().NotContain("// Total = aggregate over all");
    }

    private static string Render(QueryViewModel model)
    {
        var template = new QueryViewBuildTemplate(model);
        var artifact = template.RenderOutput();
        artifact.IsEmpty.Should().BeFalse();
        return artifact.Text;
    }

    /// <summary>
    ///     Counts initializations of a view member inside the <c>.Select(g =&gt; new View { ... })</c>
    ///     object initializer only — excluding the <c>.GroupBy(e =&gt; new { ... })</c> anonymous key,
    ///     where the same name legitimately appears. CS1912 is about duplicates within the SAME
    ///     object initializer, so this is the block that must contain each member at most once.
    /// </summary>
    private static int CountMemberInitializers(string source, string memberName)
    {
        var selectIndex = source.IndexOf(".Select(", StringComparison.Ordinal);
        selectIndex.Should().BeGreaterThanOrEqualTo(0, "the grouped Build method must contain a .Select projection");
        var selectBlock = source[selectIndex..];
        return Regex.Matches(selectBlock, $@"(?<![A-Za-z0-9_]){Regex.Escape(memberName)}\s*=").Count;
    }
}
