using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The <c>DbSet</c> that <c>[ReadAccess&lt;T&gt;]</c> adds to a reading boundary carries the named
///     <c>"Tenant"</c> query filter when the entity is <c>ITenantEntity</c>.
/// </summary>
/// <remarks>
///     <para>
///         An entity crossing through <c>[ReadAccess]</c> brings its owner's per-entity config with it,
///         so <c>"SoftDelete"</c> and the visibility rules cross the line. <c>"Tenant"</c> does not live
///         there: it needs the scoped <c>ITenantContext</c>, so each boundary's <c>OnModelCreating</c>
///         installs it — and it installed it for that context's own entities only. The reader's raw
///         <c>Set&lt;T&gt;()</c> therefore carried <c>!IsDeleted</c> and nothing else, and read every
///         tenant's rows. Measured from outside first, on a consumer application.
///     </para>
///     <para>
///         ⚠️ These four cases measure the <b>template</b>, from a model built by hand. The other half of
///         the defect was the transform: <c>DbContextFeature.BuildReadAccessEntities</c> never set
///         <c>IsTenantEntity</c>, so a correct template would still have received <c>false</c>. That half
///         is measured where it can only be measured for real —
///         <c>Showcase.IntegrationTests</c> <c>BoundaryTests.TheReadAccessSet_IsTenantFilteredLikeTheOwnersSet</c>,
///         over Postgres, on <c>Property</c>, which is genuinely <c>ITenantEntity</c> and genuinely
///         read across the boundary by <c>BookingBoundary</c>.
///     </para>
/// </remarks>
public class ReadAccessTenantFilterTests
{
    private const string OwnedEntity = "MyApp.Sales.Entities.Order";
    private const string ReadEntity = "MyApp.Catalog.Entities.CatalogItem";

    private static BoundaryDbContextModel BuildModel(
        bool ownedIsTenantEntity, bool readAccessIsTenantEntity) => new()
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
                FullTypeName = OwnedEntity,
                TypeName = "Order",
                DbSetName = "Orders",
                ConfigurationTypeName = "OrderEntityConfig",
                IsTenantEntity = ownedIsTenantEntity,
            },
        }.ToEquatableArray(),
        ReadAccessEntities = new[]
        {
            new DbContextEntityModel
            {
                FullTypeName = ReadEntity,
                TypeName = "CatalogItem",
                DbSetName = "CatalogItems",
                ConfigurationTypeName = "CatalogItemEntityConfig",
                IsTenantEntity = readAccessIsTenantEntity,
            },
        }.ToEquatableArray(),
    };

    private static string Render(bool ownedIsTenantEntity, bool readAccessIsTenantEntity)
        => new BoundaryDbContextTemplate(BuildModel(ownedIsTenantEntity, readAccessIsTenantEntity))
            .RenderOutput().Text;

    [Fact]
    public void ATenantEntityReadAcrossTheBoundary_GetsTheTenantQueryFilter()
    {
        var source = Render(ownedIsTenantEntity: false, readAccessIsTenantEntity: true);

        source.Should().Contain(
            $"modelBuilder.Entity<{ReadEntity}>().HasQueryFilter(\"Tenant\"",
            "the reader's own context is the only place that can install it");
    }

    /// <summary>
    ///     The control: an entity read across the boundary that is not tenant-scoped gets no filter.
    /// </summary>
    /// <remarks>
    ///     Without it the assertion above would hold on a template that filtered every read-access
    ///     entity, which would not compile for one with no <c>TenantId</c>.
    /// </remarks>
    [Fact]
    public void ANonTenantEntityReadAcrossTheBoundary_GetsNoTenantQueryFilter()
    {
        var source = Render(ownedIsTenantEntity: true, readAccessIsTenantEntity: false);

        source.Should().Contain($"modelBuilder.Entity<{OwnedEntity}>().HasQueryFilter(\"Tenant\"",
            "the owned tenant entity is the control that the block is emitted at all");
        source.Should().NotContain($"modelBuilder.Entity<{ReadEntity}>().HasQueryFilter(\"Tenant\"");
    }

    /// <summary>
    ///     A boundary whose <b>only</b> tenant entity arrives through <c>[ReadAccess]</c> still declares
    ///     the <c>ITenantContext</c> field the filter reads.
    /// </summary>
    /// <remarks>
    ///     Gating the field on <c>HasTenantEntities</c>, which looks at owned entities alone, would emit a
    ///     filter naming a field that does not exist for this shape. ⚠️ It is gated
    ///     on <c>RequiresTenantContext</c> and not on a widened <c>HasTenantEntities</c> because two
    ///     ad-hoc tables key their own tenant filter off the latter — see the remarks on both.
    /// </remarks>
    [Fact]
    public void TenantOnlyThroughReadAccess_StillDeclaresTheTenantContext()
    {
        var source = Render(ownedIsTenantEntity: false, readAccessIsTenantEntity: true);

        source.Should().Contain("_tenantContext",
            "the emitted filter reads it, so the field and the ctor parameter have to be there");
        source.Should().Contain("global::Pragmatic.MultiTenancy.ITenantContext? tenantContext");
    }

    [Fact]
    public void NoTenantEntityAtAll_EmitsNoTenantQueryFilter()
    {
        var source = Render(ownedIsTenantEntity: false, readAccessIsTenantEntity: false);

        source.Should().NotContain("HasQueryFilter(\"Tenant\"");
    }

    /// <summary>
    ///     A read-access tenant entity does not make the boundary's own ad-hoc tables tenant-scoped.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Written after breaking it. The first version of this fix widened
    ///     <c>HasTenantEntities</c>, and two other renderers read that property to decide whether
    ///     <c>__SagaInstances</c> and <c>__BatchProgress</c> — tables that are not in
    ///     <c>Entities</c> — get a tenant filter of their own. Those filters are fail-closed, so a
    ///     boundary that owns no tenant rows but reads one suddenly hid every saga from any reader
    ///     without a resolved tenant: five saga tests in the Showcase went red at once. Reading rows
    ///     that belong to a tenant is not the same as storing them.
    /// </remarks>
    [Fact]
    public void TenantOnlyThroughReadAccess_LeavesTheBoundarysOwnTablesUnscoped()
    {
        var model = BuildModel(ownedIsTenantEntity: false, readAccessIsTenantEntity: true) with
        {
            HasBatchProgress = true,
            HasSagaPersistence = true,
        };

        var source = new BoundaryDbContextTemplate(model).RenderOutput().Text;

        source.Should().Contain($"modelBuilder.Entity<{ReadEntity}>().HasQueryFilter(\"Tenant\"",
            "the control: the read-access entity itself is filtered");
        source.Should().NotContain("Pragmatic.Messaging.Saga.SagaInstance>().HasQueryFilter",
            "this boundary stores no tenant rows of its own, and the filter is fail-closed");
        source.Should().NotContain("Pragmatic.Messaging.Batch.BatchProgress>().HasQueryFilter");
    }
}
