using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The four diagnostics that answer a declaration the generator cannot honour.
/// </summary>
/// <remarks>
///     <para>
///         Each one replaces a silence. Without them, <c>FilterOperator.Between</c> would fall through
///         every operator switch and come out as <c>==</c>; <c>[CascadeOn]</c> without the convention
///         foreign key would produce a handler that filters on a member the entity does not have; the
///         unread <c>[Join]</c> arguments would read as configuration and configure nothing; a result
///         type without <c>[GenerateProjection]</c> would make the query name a <c>Projection</c> that
///         does not exist.
///     </para>
///     <para>
///         They are tested end to end — the generator runs and the diagnostic is asserted on its
///         output — because the failure that matters is not a wrong descriptor, it is a descriptor that
///         is never reached.
///     </para>
/// </remarks>
public class InertDeclarationDiagnosticTests
{
    [Fact]
    public void PRAG0701_BetweenOperator_IsReported()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            public partial class RangeQuery
            {
                [Filter(Operator = FilterOperator.Between, MapTo = "Amount")]
                public List<decimal>? AmountRange { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0701").Should().BeTrue(
            "no generator renders Between, so declaring it has to stop the build rather than silently "
            + "become an equality comparison");
    }

    [Fact]
    public void PRAG0701_StaysSilentOnTheShapeThatWorks()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            public partial class RangeQuery
            {
                [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Amount")]
                public decimal? MinAmount { get; init; }

                [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "Amount")]
                public decimal? MaxAmount { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0701").Should().BeFalse(
            "two properties over one column is the shape the diagnostic recommends");
    }

    /// <summary>
    ///     ⚠️ This case holds, and the reason is the point: <c>Type</c> and <c>Alias</c> generate —
    ///     but only on a <b>key</b> join. Over a <c>Via</c> they are inert, because a resolved
    ///     <c>Via</c> contributes an include path and an include has no join type and no second source
    ///     to name. The declaration below is a <c>Via</c> join, so the count is two.
    /// </summary>
    /// <remarks>
    ///     <c>ForeignKey</c> is not reported by this diagnostic: a key join is generated, and
    ///     <c>AKeyJoinReachesAnEntityWithNoNavigationTests</c> covers it.
    /// </remarks>
    [Fact]
    public void PRAG0703_JoinOptionsThatChangeNothingOverAVia_AreReported()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Other", Type = JoinType.Left, Alias = "o")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        var reported = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0703")
            .Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        reported.Should().HaveCount(2,
            "over a navigation there is no join for Type to shape and no second source for Alias to "
            + "name — which is exactly what stops being true when the join is declared by key");
        reported.Should().Contain(m => m.Contains("Type = JoinType.Left", System.StringComparison.Ordinal));
        reported.Should().Contain(m => m.Contains("Alias", System.StringComparison.Ordinal));
    }

    [Fact]
    public void PRAG0703_StaysSilentOnAJoinThatOnlyDeclaresVia()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            [Join<Other>(Via = "Other")]
            public partial class JoinQuery
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeFalse(
            "Via is the one argument a template reads");
    }

    [Fact]
    public void PRAG0702_CascadeWithoutTheConventionForeignKey_IsReported()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Follower : IEntity
            {
                [CascadeOn<Thing>(nameof(Thing.Name))]
                public string ThingName { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0702").Should().BeTrue(
            "the handler filters on Follower.ThingId, which Follower does not declare");
    }

    [Fact]
    public void PRAG0702_StaysSilentWhenTheForeignKeyIsThere()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            [Relation.ManyToOne<Thing>]
            public partial class Follower : IEntity
            {

                [CascadeOn<Thing>(nameof(Thing.Name))]
                public string ThingName { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0702").Should().BeFalse(
            "with the foreign key present the handler has something to filter on");
    }

    [Fact]
    public void PRAG0704_ResultTypeWithoutGenerateProjection_IsReported()
    {
        var result = Run("""
            [MapFrom<Thing>]
            public partial class ThingDto
            {
                public Guid Id { get; init; }
            }

            [Query<Thing, ThingDto>]
            public partial class ListThings
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeTrue(
            "[MapFrom<T>] gives FromEntity and Selector; the Projection the query names comes from "
            + "[GenerateProjection], and without it the generated file does not compile");
    }

    [Fact]
    public void PRAG0704_StaysSilentWithBothAttributes()
    {
        var result = Run("""
            [MapFrom<Thing>]
            [GenerateProjection]
            public partial class ThingDto
            {
                public Guid Id { get; init; }
            }

            [Query<Thing, ThingDto>]
            public partial class ListThings
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeFalse();
    }

    [Fact]
    public void PRAG0704_StaysSilentWhenTheQueryAnswersWithTheEntity()
    {
        var result = Run("""
            [Query<Thing>]
            public partial class ListThings
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeFalse(
            "there is no projection to look for when the result is the entity");
    }

    /// <summary>
    ///     <c>[SearchAcross]</c> on a query searches its columns. What is still inert is one on
    ///     a property that is not text: there is nothing to search with.
    /// </summary>
    [Fact]
    public void PRAG0703_SearchAcrossOnAQueryPropertyThatIsNotText_IsReported()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            public partial class SearchThings
            {
                [SearchAcross("Name")]
                public int? Search { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeTrue(
            "a search matches text, and an int? has none to match");
    }

    /// <summary>The control: on a string it is honoured, and nothing is reported.</summary>
    [Fact]
    public void PRAG0703_StaysSilentForASearchAcrossOnText()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            public partial class SearchThings
            {
                [SearchAcross("Name")]
                public string? Search { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeFalse();
    }

    [Fact]
    public void PRAG0703_FilterGroupOnAQuery_IsReported()
    {
        var result = Run("""
            [FilterDto<Thing>]
            public partial class ThingFilter
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Name { get; init; }
            }

            [Query<Thing, Thing>]
            public partial class GroupThings
            {
                [FilterGroup(FilterLogic.Or)]
                public ThingFilter? Group { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeTrue(
            "[FilterGroup] is read inside a filter DTO, not on a query");
    }

    [Fact]
    public void PRAG0703_FilterableHandler_IsReported()
    {
        var result = Run("""
            public sealed class MyHandler;

            [GridFilter<Thing>]
            public partial class ThingGrid
            {
                [Filterable(Handler = typeof(MyHandler))]
                public string? Name { get; set; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeTrue(
            "the handler reaches the model and no template calls it");
    }

    [Fact]
    public void PRAG0703_JoinOnAQueryView_IsReported()
    {
        var result = Run("""
            [QueryView<Thing>]
            [Join<Other>(Via = "Other")]
            [GroupBy<Thing>(Properties = "Name")]
            public partial class ThingTotals
            {
                [From<Thing>(Property = "Name")]
                public string Name { get; set; } = "";

                [Count<Thing>]
                public int Count { get; set; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeTrue(
            "a view reads [GroupBy], [From] and the aggregates, and nothing else");
    }

    [Fact]
    public void PRAG0703_ProcessorsOnAQuery_AreReported()
    {
        var result = Run("""
            public sealed class Audit : Pragmatic.Endpoints.Processors.IEndpointPreProcessor
            {
                public Task<Pragmatic.Result.VoidResult<Pragmatic.Result.IError>> ProcessAsync(
                    Pragmatic.Endpoints.Processors.EndpointContext context, CancellationToken ct)
                    => throw new NotImplementedException();
            }

            [Query<Thing, Thing>]
            [PreProcessor<Audit>]
            public partial class ListThings
            {
                [Filter]
                public string? Name { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeTrue(
            "the query handler template does not read them: on a query they run never");
    }

    /// <summary>
    ///     One entity, one boundary, and whatever the test adds. Real attributes rather than stubs: a
    ///     stub that drifts from the attribute it imitates makes the generator see nothing and the test
    ///     pass for it.
    /// </summary>
    [Fact]
    public void PRAG0707_AnInputThatIsNeitherFilterNorRequiredNorNullable_IsReported()
    {
        var result = Run("""
            [Query<Thing, Thing>(Single = true)]
            public partial class GetThingQuery
            {
                public Guid Id { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0707").Should().BeTrue(
            "nothing is generated for it: Apply returns the query untouched and the endpoint answers 200 "
            + "with whichever row comes first");
    }

    [Fact]
    public void PRAG0707_StaysSilentOnRequired()
    {
        var result = Run("""
            [Query<Thing, Thing>(Single = true)]
            public partial class GetThingQuery
            {
                public required Guid Id { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0707").Should().BeFalse();
    }

    [Fact]
    public void PRAG0707_StaysSilentOnNullable()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            public partial class FindThingsQuery
            {
                public Guid? Id { get; init; }
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0707").Should().BeFalse(
            "paging properties are recognised by name and are not inputs that were meant to filter");
    }

    /// <remarks>
    ///     A get-only property is not something the caller supplies, so it was never going to be a
    ///     filter and reporting it would be noise on a shape that is perfectly correct.
    /// </remarks>
    [Fact]
    public void PRAG0707_StaysSilentOnAComputedProperty()
    {
        var result = Run("""
            [Query<Thing, Thing>]
            public partial class FindThingsQuery
            {
                public string? Name { get; init; }
                public bool IsUnfiltered => Name is null;
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0707").Should().BeFalse();
    }

    [Fact]
    public void PRAG0708_HierarchyWithNoSelfRelation_IsReported()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            [GenerateHierarchy]
            public partial class Node : IEntity
            {
                public Guid ManagerId { get; private set; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0708").Should().BeTrue(
            "the tree is a declared relation from the entity to itself, and a hand-written ManagerId is "
            + "not one — the attribute used to generate nothing at all here");
    }

    [Fact]
    public void PRAG0708_StaysSilentOnADeclaredSelfRelation()
    {
        var result = Run("""
            [Entity]
            [BelongsTo<ThingBoundary>]
            [Relation.ManyToOne<Node>.WithNavigation("Parent", Required = false)]
            [GenerateHierarchy]
            public partial class Node : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0708").Should().BeFalse();
    }

    private static SourceGenRunResult Run(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Endpoints.Attributes;

            namespace Inert;

            public sealed class ThingBoundary;

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Thing : IEntity
            {
                [CascadeSource]
                public string Name { get; private set; } = "";
                public decimal Amount { get; private set; }
            }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Other : IEntity
            {
                public string Note { get; private set; } = "";
            }

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
