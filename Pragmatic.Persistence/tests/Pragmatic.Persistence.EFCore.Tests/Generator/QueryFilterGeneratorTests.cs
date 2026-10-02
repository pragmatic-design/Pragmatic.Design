using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Tests for auto-generated IQueryFilter implementations:
///     - SoftDeleteFilter_{TypeName} for [SoftDelete] entities
///     - TenantFilter_{TypeName} for ITenantEntity entities
///     - QueryFilterRegistrationExtensions for DI registration
/// </summary>
public class QueryFilterGeneratorTests
{
    [Fact]
    public void SoftDeleteEntity_GeneratesSoftDeleteFilter()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [SoftDelete]
            public partial class Order : ISoftDelete
            {
                public bool IsDeleted { get; set; }
                public System.DateTimeOffset? DeletedAt { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "SoftDeleteFilter");
        generated.Should().NotBeNull();
        generated.Should().Contain("SoftDeleteFilter");
        generated.Should().Contain("IQueryFilter<global::TestApp.Order>");
        generated.Should().Contain("entity => !entity.IsDeleted");
        generated.Should().Contain("Priority");
        generated.Should().Contain("100");
        generated.Should().Contain("FilterScope.Default");
    }

    [Fact]
    public void TenantEntity_GeneratesTenantFilter()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.MultiTenancy;

            namespace TestApp;

            [Entity]
            public partial class Invoice : ITenantEntity
            {
                public string TenantId { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "TenantFilter");
        generated.Should().NotBeNull();
        generated.Should().Contain("TenantFilter");
        generated.Should().Contain("IQueryFilter<global::TestApp.Invoice>");
        // Pin the FULL fail-closed expression, not just the "==" half. An unresolved tenant
        // (TenantId == null) must match NO rows; a regression that drops the "!= null" guard would
        // turn the filter fail-OPEN (cross-tenant data leak) yet still satisfy a "== " -only assert.
        generated.Should().Contain(
            "tenantContext.TenantId != null && entity.TenantId == tenantContext.TenantId");
        generated.Should().Contain("Priority");
        generated.Should().Contain("200");
        generated.Should().Contain("FilterScope.All");
        generated.Should().Contain("ITenantContext tenantContext");
    }

    [Fact]
    public void SoftDeleteAndTenantEntity_GeneratesBothFilters()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.MultiTenancy;

            namespace TestApp;

            [Entity]
            [SoftDelete]
            public partial class Document : ISoftDelete, ITenantEntity
            {
                public bool IsDeleted { get; set; }
                public System.DateTimeOffset? DeletedAt { get; set; }
                public string TenantId { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var softDeleteGenerated = GeneratorTestHelper.GetGeneratedSource(result, "SoftDeleteFilter");
        softDeleteGenerated.Should().NotBeNull();
        softDeleteGenerated.Should().Contain("SoftDeleteFilter");

        var tenantGenerated = GeneratorTestHelper.GetGeneratedSource(result, "TenantFilter");
        tenantGenerated.Should().NotBeNull();
        tenantGenerated.Should().Contain("TenantFilter");
    }

    [Fact]
    public void SoftDeleteEntity_GeneratesQueryFilterRegistration()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [SoftDelete]
            public partial class Product : ISoftDelete
            {
                public bool IsDeleted { get; set; }
                public System.DateTimeOffset? DeletedAt { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var registration = GeneratorTestHelper.GetGeneratedSource(result, "Persistence.QueryFilters");
        registration.Should().NotBeNull();
        registration.Should().Contain("AddSingleton<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, ");
        registration.Should().Contain("Product.SoftDeleteFilter");
        registration.Should().Contain("IQueryFilterProvider");
        registration.Should().Contain("IQueryFilterToggle");
    }

    [Fact]
    public void TenantEntity_GeneratesScopedRegistration()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.MultiTenancy;

            namespace TestApp;

            [Entity]
            public partial class Booking : ITenantEntity
            {
                public string TenantId { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var registration = GeneratorTestHelper.GetGeneratedSource(result, "Persistence.QueryFilters");
        registration.Should().NotBeNull();
        registration.Should().Contain("AddScoped<global::Pragmatic.Persistence.Query.Filters.IQueryFilter, ");
        registration.Should().Contain("Booking.TenantFilter");
    }

    [Fact]
    public void EntityWithoutSoftDeleteOrTenant_NoFilterGenerated()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            public partial class SimpleEntity
            {
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var allSources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        allSources.Keys.Should().NotContain(k => k.Contains("SoftDeleteFilter"));
        allSources.Keys.Should().NotContain(k => k.Contains("TenantFilter"));
    }

    [Fact]
    public void SoftDeleteEntity_GeneratesNamedQueryFilterInEntityConfiguration()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [SoftDelete]
            public partial class TaskItem : ISoftDelete
            {
                public bool IsDeleted { get; set; }
                public System.DateTimeOffset? DeletedAt { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // EntityConfiguration is now generated at host level only (DbContextFeature),
        // so module-level tests should NOT expect it.
        var entityConfig = GeneratorTestHelper.GetGeneratedSource(result, "EntityConfig");
        entityConfig.Should().BeNull("EntityConfig is generated at host level, not module level");
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, GetReferences());
    }

    private static MetadataReference[] GetReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<IQueryFilter>(),
            GeneratorTestHelper.FromType<ITenantEntity>(),
            GeneratorTestHelper.FromType<SoftDeleteAttribute>(),
        ];
    }
}
