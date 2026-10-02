using System;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Resource.Models;
using Pragmatic.SourceGenerator.Features.Resource.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     The <c>[Resource]</c> snapshot suite covers the DTOs and the four plain CRUD actions. These tests
///     cover the rest: the List/Search query classes, the <c>QueryModel</c>s injected into
///     <c>QueryFeature</c> (which is what actually makes those queries filterable/pageable), and the
///     <c>ReadBy{LogicKey}</c> action emitted for an entity with an
///     <c>[GeneratedValue]</c>/<c>[LogicKey]</c>.
/// </summary>
public class ResourceQueryAndLogicKeyTests
{
    private static ResourceCrudModel Model(string? logicKeyName = null, string? logicKeyType = null) => new()
    {
        Resource = new ResourceModel
        {
            Namespace = "Showcase.Booking.Entities",
            TypeName = "Guest",
            FullTypeName = "global::Showcase.Booking.Entities.Guest",
            Segment = "guests",
            Capabilities = 63,
            BoundaryFullTypeName = "global::Showcase.Booking.BookingBoundary",
            BoundaryName = "Booking",
            IdType = "System.Guid"
        },
        LogicKeyName = logicKeyName,
        LogicKeyType = logicKeyType,
        Properties = ImmutableArray.Create(
            new ResourcePropertyInfo { Name = "Id", TypeName = "Guid", IsPrimaryKey = true },
            new ResourcePropertyInfo { Name = "Code", TypeName = "string", IsRequired = true },
            new ResourcePropertyInfo { Name = "FirstName", TypeName = "string", IsRequired = true },
            new ResourcePropertyInfo { Name = "Phone", TypeName = "string?", IsNullable = true },
            new ResourcePropertyInfo { Name = "OwnerId", TypeName = "string", IsOwnership = true },
            new ResourcePropertyInfo { Name = "IsDeleted", TypeName = "bool", IsSoftDelete = true },
            new ResourcePropertyInfo { Name = "CreatedAt", TypeName = "DateTimeOffset", IsAudit = true })
    };

    private static string Render(ResourceQueryKind kind)
        => new ResourceQueryTemplate(Model(), kind).RenderOutput().Text;

    /// <summary>
    ///     Only the kinds <c>ResourceFeature.GenerateCrud</c> actually emits may exist: a kind with no
    ///     model builder behind it renders a query class whose <c>Apply()</c> would never be generated.
    /// </summary>
    /// <remarks>
    ///     Read and ReadByLogicKey joined the list when the scaffolded reads stopped being
    ///     <c>DomainAction</c>s calling the repository by hand. Both are <c>Single = true</c>, which is
    ///     what makes "no such row" a 404 instead of a 200 with nothing in it.
    /// </remarks>
    [Fact]
    public void ResourceQueryKind_OnlyDeclaresTheKindsThatAreActuallyGenerated()
        => Enum.GetNames(typeof(ResourceQueryKind)).Should()
            .Equal("List", "Search", "Read", "ReadByLogicKey");

    // ---------------------------------------------------------------- List

    [Fact]
    public void ListQuery_DeclaresAPagedPartialQueryOverTheListItemDto()
    {
        var source = Render(ResourceQueryKind.List);

        source.Should().Contain("namespace Showcase.Booking.Entities");
        source.Should().Contain("[Query<Guest, global::Showcase.Booking.Entities.GuestListItemDto>]");
        source.Should().Contain("public partial class ResourceListGuestQuery");
        source.Should().Contain("public int Page { get; init; } = 1;");
        source.Should().Contain("public int PageSize { get; init; } = 20;");
        source.Should().NotContain("[Filter", "the plain list query exposes no filters");
    }

    [Fact]
    public void ListQuery_HintNameIsScopedToTheEntityAndTheKind()
    {
        new ResourceQueryTemplate(Model(), ResourceQueryKind.List).RenderOutput().HintName
            .Should().Be("_Resource.Guest.List.g.cs");
    }

    [Fact]
    public void ListQueryModel_MatchesTheGeneratedClassAndMarksThePagingProperties()
    {
        var model = ResourceQueryTemplate.BuildListQueryModel(Model());

        model.TypeName.Should().Be("ResourceListGuestQuery");
        model.EntityTypeFullName.Should().Be("Showcase.Booking.Entities.Guest");
        model.ResultTypeFullName.Should().Be("Showcase.Booking.Entities.GuestListItemDto");
        model.Properties.Should().HaveCount(2);
        model.Properties.Single(p => p.PropertyName == "Page").IsPageProperty.Should().BeTrue();
        model.Properties.Single(p => p.PropertyName == "PageSize").IsPageSizeProperty.Should().BeTrue();
    }

    // ---------------------------------------------------------------- Search

    [Fact]
    public void SearchQuery_TurnsEveryStringPropertyIntoAContainsFilter()
    {
        var source = Render(ResourceQueryKind.Search);

        source.Should().Contain("public partial class ResourceSearchGuestQuery");
        source.Should().Contain("[Filter(Operator = FilterOperator.Contains)]");
        source.Should().Contain("public string? Code { get; init; }");
        source.Should().Contain("public string? FirstName { get; init; }");

        // An optional text column is exactly the kind of field a user searches on, so a nullable
        // string ("string?") is searchable too.
        source.Should().Contain("public string? Phone { get; init; }");
        // Identity, ownership, soft-delete and audit columns are never search inputs.
        source.Should().NotContain("OwnerId");
        source.Should().NotContain("IsDeleted");
        source.Should().NotContain("CreatedAt");

        source.Should().Contain(
            "[Sort(DefaultDirection = global::Pragmatic.Persistence.Query.SortDirection.Ascending)]");
        source.Should().Contain("public SortDirection? IdSort { get; init; }");
        source.Should().Contain("public int Page { get; init; } = 1;");
    }

    [Fact]
    public void SearchQueryModel_MirrorsTheGeneratedPropertiesOneForOne()
    {
        var model = ResourceQueryTemplate.BuildSearchQueryModel(Model());

        model.TypeName.Should().Be("ResourceSearchGuestQuery");
        model.Properties.Select(p => p.PropertyName).Should()
            .Equal("Code", "FirstName", "Phone", "IdSort", "Page", "PageSize");

        var code = model.Properties[0];
        code.IsFilter.Should().BeTrue();
        code.Operator.Should().Be(FilterOperatorKind.Contains);
        code.PropertyType.Should().Be("string?");
        code.IsNullable.Should().BeTrue();

        // The entity column behind Phone is itself nullable — the apply layer has to know.
        model.Properties.Single(p => p.PropertyName == "Phone").EntityPathIsNullable.Should().BeTrue();
        code.EntityPathIsNullable.Should().BeFalse();

        var sort = model.Properties.Single(p => p.PropertyName == "IdSort");
        sort.IsSort.Should().BeTrue();
        sort.DefaultSortDirection.Should().Be(SortDirectionKind.Ascending);
    }

    /// <summary>
    ///     Searching a nullable column must not depend on the provider: <c>e.Phone.Contains(v)</c> is
    ///     fine in SQL but throws the moment the same expression runs over objects (in-memory executor,
    ///     unit tests), and it does not even compile clean under a nullable context.
    /// </summary>
    [Fact]
    public void SearchQueryApply_NullableColumnFilter_IsNullGuarded()
    {
        var source = new QueryApplyTemplate(ResourceQueryTemplate.BuildSearchQueryModel(Model()))
            .RenderOutput().Text;

        source.Should().Contain("query = query.Where(e => e.Phone != null && e.Phone.Contains(this.Phone!));");
        source.Should().Contain("query = query.Where(e => e.Code.Contains(this.Code!));");
    }

    [Fact]
    public void SearchQuery_EntityWithNoStringProperties_StillPagesAndSorts()
    {
        var numericOnly = Model() with
        {
            Properties = ImmutableArray.Create(
                new ResourcePropertyInfo { Name = "Id", TypeName = "Guid", IsPrimaryKey = true },
                new ResourcePropertyInfo { Name = "Seats", TypeName = "int", IsRequired = true })
        };

        var source = new ResourceQueryTemplate(numericOnly, ResourceQueryKind.Search).RenderOutput().Text;
        var model = ResourceQueryTemplate.BuildSearchQueryModel(numericOnly);

        source.Should().NotContain("[Filter");
        source.Should().Contain("public int PageSize { get; init; } = 20;");
        model.Properties.Select(p => p.PropertyName).Should().Equal("IdSort", "Page", "PageSize");
    }

    [Fact]
    public void SearchQuery_HintNameIsScopedToTheEntityAndTheKind()
    {
        new ResourceQueryTemplate(Model(), ResourceQueryKind.Search).RenderOutput().HintName
            .Should().Be("_Resource.Guest.Search.g.cs");
    }

    // ---------------------------------------------------------------- ReadBy{LogicKey}

    /// <summary>
    ///     Reading by the logic key is a Single query filtered on it, answering the read DTO.
    /// </summary>
    /// <remarks>
    ///     A query, not a DomainAction calling the repository with a hand-built specification: as a
    ///     query it goes through the same pipeline as every other read — global filters included — and
    ///     <c>Single = true</c> makes "no such row" a 404 rather than an empty body.
    /// </remarks>
    [Fact]
    public void ReadByLogicKeyQuery_FiltersOnTheLogicKeyAndAnswersTheReadDto()
    {
        var source = new ResourceQueryTemplate(Model("Code", "string"), ResourceQueryKind.ReadByLogicKey)
            .RenderOutput().Text;

        source.Should().Contain("public partial class ResourceReadByCodeGuestQuery");
        source.Should().Contain("[Query<Guest, global::Showcase.Booking.Entities.GuestReadDto>(Single = true)]");
        source.Should().Contain("public string Code { get; init; }");

        var model = ResourceQueryTemplate.BuildReadByLogicKeyQueryModel(Model("Code", "string"));
        model.Properties.AsImmutableArray().Should().ContainSingle()
            .Which.PropertyName.Should().Be("Code");
        model.ResultTypeName.Should().Be("GuestReadDto");
    }

    /// <summary>
    ///     The two reads must not collide on a hint name.
    /// </summary>
    /// <remarks>
    ///     A duplicate hint is not an error Roslyn stops on: it discards the <b>entire</b> output of the
    ///     generator and reports CS8785 as a warning. Without <c>--warnaserror</c> the build passes with
    ///     every generated file missing.
    /// </remarks>
    [Fact]
    public void TheTwoReads_DoNotCollideOnAHintName()
    {
        var byId = new ResourceQueryTemplate(Model("Code", "string"), ResourceQueryKind.Read).RenderOutput();
        var byKey = new ResourceQueryTemplate(Model("Code", "string"), ResourceQueryKind.ReadByLogicKey)
            .RenderOutput();

        byId.HintName.Should().Be("_Resource.Guest.Read.g.cs");
        byKey.HintName.Should().Be("_Resource.Guest.ReadByCode.g.cs");
    }

    [Fact]
    public void ReadByLogicKeyQuery_NonStringLogicKey_IsTyped()
    {
        var source = new ResourceQueryTemplate(Model("ExternalId", "int"), ResourceQueryKind.ReadByLogicKey)
            .RenderOutput().Text;

        source.Should().Contain("public int ExternalId { get; init; }");
        source.Should().Contain("ResourceReadByExternalIdGuestQuery");
    }

    [Fact]
    public void ReadByLogicKeyQuery_MissingLogicKeyType_FallsBackToString()
        => new ResourceQueryTemplate(Model("Code"), ResourceQueryKind.ReadByLogicKey).RenderOutput().Text
            .Should().Contain("public string Code { get; init; }");
}
