using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The soft-delete partial unique index on the logic key uses raw SQL in <c>HasFilter</c>, whose
///     quoting and boolean literals are provider-specific. The filter must be emitted at Host level
///     (BoundaryDbContext) branched on EfCoreProvider — PostgreSQL syntax hardcoded in the
///     EntityConfig breaks SQL Server migrations.
/// </summary>
public class LogicKeyFilteredIndexTests
{
    private static BoundaryDbContextModel BuildModel(EfCoreProvider provider) => new()
    {
        Namespace = "MyApp.Sales.Entities",
        ClassName = "SalesDbContext",
        BoundaryName = "Sales",
        BoundaryTypeName = "MyApp.Sales.SalesBoundary",
        EfCoreProvider = provider,
        Entities = new[]
        {
            new DbContextEntityModel
            {
                FullTypeName = "MyApp.Sales.Entities.Product",
                TypeName = "Product",
                DbSetName = "Products",
                ConfigurationTypeName = "ProductEntityConfig",
                LogicKeyPropertyName = "Sku",
                IsSoftDelete = true,
            },
        }.ToEquatableArray(),
    };

    // EfCoreProvider is internal — xUnit theory members must be public, hence per-provider facts.
    // Expected strings are the generated C# SOURCE text, so quoted identifiers appear escaped (\").
    [Fact]
    public void DbContext_SoftDeleteLogicKey_PostgreSql_EmitsQuotedBooleanFilter()
        => AssertFilter(EfCoreProvider.PostgreSql, "HasFilter(\"\\\"IsDeleted\\\" = false\")");

    [Fact]
    public void DbContext_SoftDeleteLogicKey_SqlServer_EmitsBracketedBitFilter()
        => AssertFilter(EfCoreProvider.SqlServer, "HasFilter(\"[IsDeleted] = 0\")");

    [Fact]
    public void DbContext_SoftDeleteLogicKey_Sqlite_EmitsQuotedNumericFilter()
        => AssertFilter(EfCoreProvider.Sqlite, "HasFilter(\"\\\"IsDeleted\\\" = 0\")");

    private static void AssertFilter(EfCoreProvider provider, string expectedFilter)
    {
        var source = new BoundaryDbContextTemplate(BuildModel(provider)).RenderOutput().Text;

        source.Should().Contain("HasIndex(e => e.Sku).IsUnique().HasFilter(");
        source.Should().Contain(expectedFilter);
    }

    [Fact]
    public void DbContext_NoSoftDelete_DoesNotEmitFilteredIndex()
    {
        var model = BuildModel(EfCoreProvider.PostgreSql);
        model = model with
        {
            Entities = new[]
            {
                model.Entities[0] with { IsSoftDelete = false },
            }.ToEquatableArray(),
        };

        var source = new BoundaryDbContextTemplate(model).RenderOutput().Text;

        source.Should().NotContain("HasFilter(");
    }

    [Fact]
    public void EntityConfig_SoftDeleteLogicKey_EmitsBareUniqueIndexWithoutRawSql()
    {
        // The EntityConfig must not contain provider-specific SQL: it emits the bare unique
        // index and the host merges in the provider-correct filter (same property set).
        var model = new EntityMetadataModel
        {
            TypeName = "Product",
            FullTypeName = "MyApp.Sales.Entities.Product",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            LogicKeys = ImmutableArray.Create(new LogicKeyPart { Name = "Sku", TypeName = "string" }),
            IsSoftDelete = true,
            IsValid = true,
        };

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => e.Sku).IsUnique();");
        source.Should().NotContain("HasFilter");
    }

    /// <summary>
    ///     A domain key over two properties becomes one unique index over both.
    /// </summary>
    /// <remarks>
    ///     The single-property form above is unchanged on purpose: a domain key that already had one
    ///     part must keep emitting exactly the index it emitted, so no existing schema moves when this
    ///     capability arrives. Only a second <c>[LogicKey]</c> changes anything.
    /// </remarks>
    [Fact]
    public void EntityConfig_CompositeLogicKey_EmitsOneIndexOverEveryPart()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "RoomType",
            FullTypeName = "MyApp.Catalog.Entities.RoomType",
            Namespace = "MyApp.Catalog.Entities",
            IdType = "System.Guid",
            LogicKeys = ImmutableArray.Create(
                new LogicKeyPart { Name = "PropertyId", TypeName = "System.Guid" },
                new LogicKeyPart { Name = "Code", TypeName = "string" }),
            IsValid = true,
        };

        var source = new EntityConfigurationTemplate(model).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.PropertyId, e.Code }).IsUnique();",
            "a code unique per property is one index over both, not two indexes");
    }
}
