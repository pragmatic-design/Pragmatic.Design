using System.Collections.Immutable;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Cryptography.EFCore.Entities;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The database holding a <c>[DataSubject]</c> gets the per-subject key table, as the context that
///     maps it expects.
/// </summary>
/// <remarks>
///     <para>
///         The same dual source as the subject registry and the audit trail, and the same failure if
///         the two halves disagree: the generated context applies
///         <c>CryptographyDbContext.ApplyCryptographyConfigurations</c> while the migration schema is
///         built from metadata, so a table missing here is mapped and never created — and the store
///         fails on its first read.
///     </para>
///     <para>
///         ⚠️ The keys go <b>in the same database</b> as what they protect. Destroying one is how a
///         crypto-shredded column is erased, and a key in another store would make that erasure a
///         distributed transaction with a window in which the data is readable and the erasure says it
///         is not.
///     </para>
/// </remarks>
public class SubjectKeySchemaMatchesEfModelTests
{
    private const string TableName = SubjectKeyEntityTypeConfiguration.TableName;

    private static ImmutableArray<TableSchemaModel> KeyTables(
        bool isDataSubject = true, bool hasCryptographyEFCore = true) =>
        [.. SchemaMetadataTransform
            .Transform(Entity(isDataSubject), EfCoreProvider.PostgreSql, "App", "MyApp",
                hasCryptographyEFCore: hasCryptographyEFCore)
            .Tables.AsImmutableArray()
            .Where(t => t.TableName == TableName)];

    /// <summary>The EF model, built from the configuration alone — no provider needed to read names.</summary>
    private static Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType EfType()
    {
        var builder = new ModelBuilder();
        builder.ApplyConfiguration(new SubjectKeyEntityTypeConfiguration());

        return builder.Model.GetEntityTypes().First(e => e.GetTableName() == TableName);
    }

    [Fact]
    public void ADatabaseHoldingADataSubject_HasTheKeyTable()
        => KeyTables().Select(t => t.TableName).Should().BeEquivalentTo([TableName]);

    [Fact]
    public void TheSchemaAndTheEfModel_DescribeTheSameColumns()
    {
        var schema = KeyTables()[0].Columns.AsImmutableArray();
        var model = EfType().GetProperties().ToList();

        schema.Select(c => c.Name).Order(StringComparer.Ordinal).Should()
            .Equal(model.Select(p => p.Name).Order(StringComparer.Ordinal));
        schema.Where(c => c.IsPrimaryKey).Select(c => c.Name).Order(StringComparer.Ordinal).Should()
            .Equal(EfType().FindPrimaryKey()!.Properties.Select(p => p.Name).Order(StringComparer.Ordinal));
        schema.Where(c => c.IsNullable).Select(c => c.Name).Order(StringComparer.Ordinal).Should()
            .Equal(model.Where(p => p.IsNullable).Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     The key id is unique: a decrypting reader resolves by it and has nothing else.
    /// </summary>
    /// <remarks>
    ///     The id is a fingerprint of the material, so two rows sharing one means a key was reused —
    ///     which is worse than an ambiguous lookup.
    /// </remarks>
    [Fact]
    public void TheKeyIdIndex_IsUnique()
    {
        KeyTables()[0].Indexes.AsImmutableArray()
            .Should().ContainSingle(i => i.IsUnique && i.Columns.AsImmutableArray().SequenceEqual(new[] { "KeyId" }));
    }

    /// <summary>The controls: no data subject, or no package, and the table is not there.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WithoutADataSubjectOrThePackage_ThereIsNoKeyTable(bool isDataSubject, bool hasCryptographyEFCore)
        => KeyTables(isDataSubject, hasCryptographyEFCore).Should().BeEmpty();

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
