using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A trait child of a tenant-scoped parent is filtered by the parent's tenant, read through the
///     reference navigation the trait generates.
/// </summary>
/// <remarks>
///     <para>
///         The child carries no tenant column — <c>KnowledgeItemComment</c> has a foreign key to its
///         term and nothing else — and the trait's generated list query filters by the parent's id and
///         nothing else. A caller holding an id from another tenant therefore read the whole
///         collection: content, author, dates. Measured from outside first, on a consumer application,
///         where a second workspace read this one's glossary discussion in full.
///     </para>
///     <para>
///         ⚠️ Not <c>ParentVisibilityFilter</c>, which exists for the same shape: that one honours the
///         parent's <c>view-all</c> bypass permission, and no permission may cross a tenant. It is also
///         emitted only for an owned or scoped parent, which is why a parent whose sole restriction is
///         the tenant had nothing at all.
///     </para>
/// </remarks>
public class ATraitChildInheritsItsParentsTenantTests
{
    private const string Parent = "MyApp.Sales.Entities.Order";
    private const string Child = "MyApp.Sales.Entities.OrderComment";

    private static BoundaryDbContextModel BuildModel(string? parentTenantNavigation) => new()
    {
        Namespace = "MyApp.Sales.Entities",
        ClassName = "SalesDbContext",
        BoundaryName = "Sales",
        BoundaryTypeName = "MyApp.Sales.SalesBoundary",
        EfCoreProvider = EfCoreProvider.PostgreSql,
        Entities = new[]
        {
            new DbContextEntityModel
            {
                FullTypeName = Parent,
                TypeName = "Order",
                DbSetName = "Orders",
                ConfigurationTypeName = "OrderEntityConfig",
                IsTenantEntity = true,
            },
            new DbContextEntityModel
            {
                FullTypeName = Child,
                TypeName = "OrderComment",
                DbSetName = "OrderComments",
                ConfigurationTypeName = "OrderCommentEntityConfig",
                IsTraitEntity = true,
                ParentTenantNavigation = parentTenantNavigation,
            },
        }.ToEquatableArray(),
    };

    private static string Render(string? parentTenantNavigation)
        => new BoundaryDbContextTemplate(BuildModel(parentTenantNavigation)).RenderOutput().Text;

    [Fact]
    public void ATraitChildOfATenantParent_IsFilteredThroughTheNavigation()
    {
        var source = Render("Order");

        source.Should().Contain(
            $"modelBuilder.Entity<{Child}>().HasQueryFilter(\"ParentTenant\"",
            "the child has no tenant column, so the only place the tenant can be read is the parent");
        source.Should().Contain("e.Order != null && e.Order.TenantId == _tenantContext.TenantId",
            "through the reference navigation, and never through an orphan whose parent row is gone");
    }

    /// <summary>
    ///     The control: a trait child whose parent is not tenant-scoped gets no such filter.
    /// </summary>
    /// <remarks>
    ///     Without it "the child is filtered" is satisfied by a template that filters every trait
    ///     child — which would not compile for a parent with no <c>TenantId</c>.
    /// </remarks>
    [Fact]
    public void ATraitChildOfANonTenantParent_IsNotFiltered()
    {
        var source = Render(parentTenantNavigation: null);

        source.Should().Contain($"modelBuilder.Entity<{Parent}>().HasQueryFilter(\"Tenant\"",
            "the parent is the control that the tenant block is emitted at all");
        source.Should().NotContain("HasQueryFilter(\"ParentTenant\"");
    }

    /// <summary>
    ///     A boundary whose only tenant-touching entity is a trait child still declares the
    ///     <c>ITenantContext</c> the filter reads.
    /// </summary>
    /// <remarks>
    ///     The field is gated on <c>RequiresTenantContext</c>, which looked at entities carrying a
    ///     tenant column. A trait child carries none, so the filter would have named a field that does
    ///     not exist and the generated context would not compile.
    /// </remarks>
    [Fact]
    public void ATenantOnlyThroughATraitParent_StillDeclaresTheTenantContext()
    {
        var model = BuildModel("Order") with
        {
            Entities = new[]
            {
                new DbContextEntityModel
                {
                    FullTypeName = Parent,
                    TypeName = "Order",
                    DbSetName = "Orders",
                    ConfigurationTypeName = "OrderEntityConfig",
                    IsTenantEntity = false,
                },
                new DbContextEntityModel
                {
                    FullTypeName = Child,
                    TypeName = "OrderComment",
                    DbSetName = "OrderComments",
                    ConfigurationTypeName = "OrderCommentEntityConfig",
                    IsTraitEntity = true,
                    ParentTenantNavigation = "Order",
                },
            }.ToEquatableArray(),
        };

        var source = new BoundaryDbContextTemplate(model).RenderOutput().Text;

        source.Should().Contain("global::Pragmatic.MultiTenancy.ITenantContext? tenantContext",
            "the emitted filter reads the field, so the field and the ctor parameter have to be there");
        source.Should().Contain("HasQueryFilter(\"ParentTenant\"");
    }
}
