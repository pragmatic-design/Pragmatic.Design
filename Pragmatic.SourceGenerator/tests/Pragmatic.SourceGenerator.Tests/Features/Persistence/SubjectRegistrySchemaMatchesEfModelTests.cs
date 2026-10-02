using System.Collections.Immutable;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Privacy.EFCore.Entities;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The database holding a <c>[DataSubject]</c> gets the subject registry's tables, as the context
///     that maps them expects.
/// </summary>
/// <remarks>
///     The generated context applies <c>PrivacyDbContext.ApplyPrivacyConfigurations</c>, but the
///     migration schema is built from metadata, not from the EF model: without these tables here the
///     context maps <c>__Subjects</c> and <c>__Consents</c> and the migration never creates them, and the
///     registry fails on its first query. The audit trail's tables live with the same dual source, and
///     <see cref="AuditSchemaMatchesEfModelTests" /> is the model for this one.
/// </remarks>
public class SubjectRegistrySchemaMatchesEfModelTests
{
    private static readonly string[] RegistryTableNames =
        [SubjectRecordEntityTypeConfiguration.TableName, ConsentRecordEntityTypeConfiguration.TableName];

    private static ImmutableArray<TableSchemaModel> RegistryTables(bool isDataSubject = true, bool hasPrivacyEFCore = true) =>
        [.. SchemaMetadataTransform
            .Transform(Entity(isDataSubject), EfCoreProvider.PostgreSql, "App", "MyApp", hasPrivacyEFCore: hasPrivacyEFCore)
            .Tables.AsImmutableArray()
            .Where(t => RegistryTableNames.Contains(t.TableName))];

    private static TableSchemaModel Table(string tableName) => RegistryTables().First(t => t.TableName == tableName);

    /// <summary>The EF model, built from the configurations alone — no provider needed to read names.</summary>
    private static Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType EfType(string tableName)
    {
        var builder = new ModelBuilder();
        builder.ApplyConfiguration(new SubjectRecordEntityTypeConfiguration());
        builder.ApplyConfiguration(new ConsentRecordEntityTypeConfiguration());

        return builder.Model.GetEntityTypes().First(e => e.GetTableName() == tableName);
    }

    [Fact]
    public void ADatabaseHoldingADataSubject_HasEveryTableTheRegistryReads()
    {
        RegistryTables().Select(t => t.TableName).Should().BeEquivalentTo(RegistryTableNames);
    }

    [Theory]
    [InlineData(SubjectRecordEntityTypeConfiguration.TableName)]
    [InlineData(ConsentRecordEntityTypeConfiguration.TableName)]
    public void TheSchemaAndTheEfModel_DescribeTheSameColumns(string tableName)
    {
        var schema = Table(tableName).Columns.AsImmutableArray();
        var model = EfType(tableName).GetProperties().ToList();

        schema.Select(c => c.Name).Order(StringComparer.Ordinal).Should()
            .Equal(model.Select(p => p.Name).Order(StringComparer.Ordinal));
        schema.Where(c => c.IsPrimaryKey).Select(c => c.Name).Order(StringComparer.Ordinal).Should()
            .Equal(EfType(tableName).FindPrimaryKey()!.Properties.Select(p => p.Name).Order(StringComparer.Ordinal));
        schema.Where(c => c.IsNullable).Select(c => c.Name).Order(StringComparer.Ordinal).Should()
            .Equal(model.Where(p => p.IsNullable).Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>The blind index is unique: two live subjects sharing one would make a reference ambiguous.</summary>
    [Fact]
    public void TheLookupIndex_IsUnique()
    {
        Table(SubjectRecordEntityTypeConfiguration.TableName).Indexes.AsImmutableArray()
            .Should().ContainSingle(i => i.IsUnique && i.Columns.AsImmutableArray().SequenceEqual(new[] { "LookupIndex" }));
    }

    /// <summary>The controls: no data subject, or no registry package, and the tables are not there.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WithoutADataSubjectOrTheRegistryPackage_ThereAreNoRegistryTables(bool isDataSubject, bool hasPrivacyEFCore)
    {
        RegistryTables(isDataSubject, hasPrivacyEFCore).Should().BeEmpty();
    }

    private static ImmutableArray<EntityMetadataModel> Entity(bool isDataSubject) =>
        ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Customer",
            FullTypeName = "MyApp.Sales.Entities.Customer",
            Namespace = "MyApp.Sales.Entities",
            IdType = "System.Guid",
            Accessibility = "public",
            IsValid = true,
            IsFromReference = true,
            IsDataSubject = isDataSubject,
            BoundaryName = "Sales",
            BoundaryTypeFullName = "MyApp.Sales.SalesBoundary",
        });
}
