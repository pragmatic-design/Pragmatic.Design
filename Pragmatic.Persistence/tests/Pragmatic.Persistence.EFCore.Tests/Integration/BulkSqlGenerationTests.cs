using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.EFCore.Bulk;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Unit tests that verify the SQL template strings built by BulkExecutor.BuildSqlTemplates.
///     Uses InternalsVisibleTo to directly call BuildSqlTemplates with hand-crafted metadata,
///     allowing us to verify SQL Server, PostgreSQL, and SQLite syntax without real connections.
/// </summary>
/// <remarks>
///     These tests complement the Testcontainer integration tests by:
///     1. Running instantly (no Docker required)
///     2. Verifying exact SQL syntax for each provider dialect
///     3. Validating template caching correctness (column filtering, prefix/suffix)
/// </remarks>
public class BulkSqlGenerationTests
{
    #region SQL Server — INSERT prefix

    [Fact]
    public void SqlServer_InsertPrefix_ContainsBracketQuotedColumns()
    {
        var metadata = CreateProductMetadata(ProviderType.SqlServer);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.InsertSqlPrefix.Should().Contain("INSERT INTO [BulkProducts]");
        templates.InsertSqlPrefix.Should().Contain("[PersistenceId]");
        templates.InsertSqlPrefix.Should().Contain("[Name]");
        templates.InsertSqlPrefix.Should().Contain("[Price]");
        templates.InsertSqlPrefix.Should().EndWith("VALUES ");
    }

    #endregion

    #region SQL Server — MERGE syntax

    [Fact]
    public void SqlServer_UpsertPrefix_ContainsMergeInto()
    {
        var metadata = CreateProductMetadata(ProviderType.SqlServer);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlPrefix_PK.Should().Contain("MERGE INTO [BulkProducts] AS t USING (VALUES ");
    }

    [Fact]
    public void SqlServer_UpsertSuffix_ContainsCompleteMatchAndUpdateSyntax()
    {
        var metadata = CreateProductMetadata(ProviderType.SqlServer);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().Contain("AS s(");
        templates.UpsertSqlSuffix_PK.Should().Contain("ON (t.[PersistenceId] = s.[PersistenceId])");
        templates.UpsertSqlSuffix_PK.Should().Contain("WHEN MATCHED THEN UPDATE SET");
        templates.UpsertSqlSuffix_PK.Should().Contain("t.[Name] = s.[Name]");
        templates.UpsertSqlSuffix_PK.Should().Contain("t.[Price] = s.[Price]");
        templates.UpsertSqlSuffix_PK.Should().Contain("WHEN NOT MATCHED THEN INSERT");
    }

    [Fact]
    public void SqlServer_UpsertSuffix_WithLogicKey_MatchesOnLogicKeyColumn()
    {
        var metadata = CreateLogicKeyProductMetadata(ProviderType.SqlServer);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_LK.Should().NotBeNull();
        templates.UpsertSqlSuffix_LK!.Should().Contain("ON (t.[Sku] = s.[Sku])");
        templates.UpsertSqlSuffix_LK.Should().NotContain("ON (t.[PersistenceId]");
    }

    [Fact]
    public void SqlServer_UpsertSuffix_AuditableEntity_UsesAuditParams()
    {
        var metadata = CreateCustomerMetadata(ProviderType.SqlServer);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().Contain("t.[UpdatedAt] = @audit_now");
        templates.UpsertSqlSuffix_PK.Should().Contain("t.[UpdatedBy] = @audit_user");
        // Regular columns use source row
        templates.UpsertSqlSuffix_PK.Should().Contain("t.[FullName] = s.[FullName]");
        templates.UpsertSqlSuffix_PK.Should().Contain("t.[Email] = s.[Email]");
    }

    [Fact]
    public void SqlServer_MergeSuffix_EndsWith_Semicolon()
    {
        var metadata = CreateProductMetadata(ProviderType.SqlServer);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().EndWith(";");
    }

    #endregion

    #region PostgreSQL — ON CONFLICT syntax

    [Fact]
    public void PostgreSql_InsertPrefix_ContainsDoubleQuotedIdentifiers()
    {
        var metadata = CreateProductMetadata(ProviderType.PostgreSql);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.InsertSqlPrefix.Should().Contain("INSERT INTO \"BulkProducts\"");
        templates.InsertSqlPrefix.Should().Contain("\"PersistenceId\"");
        templates.InsertSqlPrefix.Should().Contain("\"Name\"");
    }

    [Fact]
    public void PostgreSql_UpsertSuffix_ContainsOnConflictWithUppercaseExcluded()
    {
        var metadata = CreateProductMetadata(ProviderType.PostgreSql);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().Contain("ON CONFLICT (\"PersistenceId\")");
        templates.UpsertSqlSuffix_PK.Should().Contain("DO UPDATE SET");
        // PostgreSQL uses EXCLUDED (uppercase)
        templates.UpsertSqlSuffix_PK.Should().Contain("\"Name\" = EXCLUDED.\"Name\"");
        templates.UpsertSqlSuffix_PK.Should().Contain("\"Price\" = EXCLUDED.\"Price\"");
    }

    [Fact]
    public void PostgreSql_UpsertPrefix_IsSameAsInsertPrefix()
    {
        var metadata = CreateProductMetadata(ProviderType.PostgreSql);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        // PG/SQLite upsert prefix is the INSERT prefix (value rows go between prefix and suffix)
        templates.UpsertSqlPrefix_PK.Should().Be(templates.InsertSqlPrefix);
    }

    [Fact]
    public void PostgreSql_UpsertSuffix_AuditableEntity_UsesAuditParams()
    {
        var metadata = CreateCustomerMetadata(ProviderType.PostgreSql);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().Contain("\"UpdatedAt\" = @audit_now");
        templates.UpsertSqlSuffix_PK.Should().Contain("\"UpdatedBy\" = @audit_user");
        templates.UpsertSqlSuffix_PK.Should().Contain("\"FullName\" = EXCLUDED.\"FullName\"");
    }

    [Fact]
    public void PostgreSql_UpsertSuffix_NoUpdateColumns_GeneratesDoNothing()
    {
        var metadata = new CachedEntityMetadata
        {
            TableName = "KeyOnly",
            Schema = null,
            Provider = ProviderType.PostgreSql,
            Columns =
            [
                ("PersistenceId", "PersistenceId", BulkColumnRole.Key)
            ]
        };

        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().Contain("DO NOTHING");
        templates.UpsertSqlSuffix_PK.Should().NotContain("DO UPDATE");
    }

    #endregion

    #region SQLite — ON CONFLICT syntax

    [Fact]
    public void Sqlite_UpsertSuffix_UsesLowercaseExcluded()
    {
        var metadata = CreateProductMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().Contain("ON CONFLICT(\"PersistenceId\")");
        // SQLite uses lowercase 'excluded'
        templates.UpsertSqlSuffix_PK.Should().Contain("\"Name\" = excluded.\"Name\"");
        templates.UpsertSqlSuffix_PK.Should().Contain("\"Price\" = excluded.\"Price\"");
    }

    [Fact]
    public void Sqlite_UpsertSuffix_SoftDeleteColumns_NotInUpdateSet()
    {
        var metadata = CreateOrderMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlSuffix_PK.Should().NotContain("\"IsDeleted\"");
        templates.UpsertSqlSuffix_PK.Should().NotContain("\"DeletedAt\"");
        templates.UpsertSqlSuffix_PK.Should().NotContain("\"DeletedBy\"");
        // Only regular columns in UPDATE SET
        templates.UpsertSqlSuffix_PK.Should().Contain("\"OrderNumber\"");
        templates.UpsertSqlSuffix_PK.Should().Contain("\"Total\"");
    }

    #endregion

    #region Cross-provider — column filtering

    [Fact]
    public void InsertColumns_ExcludesComputedAndUpdateOnly()
    {
        var metadata = CreateCustomerMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.InsertColumns.Should().NotContain(c => c.PropertyName == "UpdatedAt");
        templates.InsertColumns.Should().NotContain(c => c.PropertyName == "UpdatedBy");
        templates.InsertColumns.Should().Contain(c => c.PropertyName == "CreatedAt");
        templates.InsertColumns.Should().Contain(c => c.PropertyName == "CreatedBy");
    }

    [Fact]
    public void UpdateColumns_ExcludesKeyInsertOnlySoftDeleteComputed()
    {
        var metadata = CreateCustomerMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpdateColumns.Should().NotContain(c => c.PropertyName == "PersistenceId");
        templates.UpdateColumns.Should().NotContain(c => c.PropertyName == "CreatedAt");
        templates.UpdateColumns.Should().NotContain(c => c.PropertyName == "CreatedBy");
        templates.UpdateColumns.Should().Contain(c => c.PropertyName == "FullName");
        templates.UpdateColumns.Should().Contain(c => c.PropertyName == "Email");
        templates.UpdateColumns.Should().Contain(c => c.PropertyName == "UpdatedAt");
        templates.UpdateColumns.Should().Contain(c => c.PropertyName == "UpdatedBy");
    }

    [Fact]
    public void MatchColumns_PK_ContainsOnlyKeyColumns()
    {
        var metadata = CreateLogicKeyProductMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.MatchColumns_PK.Should().HaveCount(1);
        templates.MatchColumns_PK[0].PropertyName.Should().Be("PersistenceId");
    }

    [Fact]
    public void MatchColumns_LK_ContainsOnlyLogicKeyColumns()
    {
        var metadata = CreateLogicKeyProductMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.MatchColumns_LK.Should().NotBeNull();
        templates.MatchColumns_LK!.Should().HaveCount(1);
        templates.MatchColumns_LK![0].PropertyName.Should().Be("Sku");
    }

    [Fact]
    public void MatchColumns_LK_NullWhenNoLogicKey()
    {
        var metadata = CreateProductMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.MatchColumns_LK.Should().BeNull();
        templates.UpsertSqlPrefix_LK.Should().BeNull();
        templates.UpsertSqlSuffix_LK.Should().BeNull();
    }

    [Fact]
    public void HasAuditUpdateColumns_TrueWhenUpdateOnlyPresent()
    {
        var metadata = CreateCustomerMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.HasAuditUpdateColumns.Should().BeTrue();
    }

    [Fact]
    public void HasAuditUpdateColumns_FalseWhenNoUpdateOnly()
    {
        var metadata = CreateProductMetadata(ProviderType.Sqlite);
        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.HasAuditUpdateColumns.Should().BeFalse();
    }

    #endregion

    #region Schema support

    [Fact]
    public void SqlServer_InsertPrefix_IncludesSchema()
    {
        var metadata = new CachedEntityMetadata
        {
            TableName = "Products",
            Schema = "sales",
            Provider = ProviderType.SqlServer,
            Columns =
            [
                ("PersistenceId", "PersistenceId", BulkColumnRole.Key),
                ("Name", "Name", BulkColumnRole.Regular)
            ]
        };

        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.InsertSqlPrefix.Should().Contain("INSERT INTO [sales].[Products]");
    }

    [Fact]
    public void PostgreSql_UpsertPrefix_IncludesSchema()
    {
        var metadata = new CachedEntityMetadata
        {
            TableName = "Products",
            Schema = "sales",
            Provider = ProviderType.PostgreSql,
            Columns =
            [
                ("PersistenceId", "PersistenceId", BulkColumnRole.Key),
                ("Name", "Name", BulkColumnRole.Regular)
            ]
        };

        var templates = BulkExecutor.BuildSqlTemplates(metadata);

        templates.UpsertSqlPrefix_PK.Should().Contain("INSERT INTO \"sales\".\"Products\"");
    }

    #endregion

    #region Metadata factories

    private static CachedEntityMetadata CreateProductMetadata(ProviderType provider) => new()
    {
        TableName = "BulkProducts",
        Schema = null,
        Provider = provider,
        Columns =
        [
            ("PersistenceId", "PersistenceId", BulkColumnRole.Key),
            ("Name", "Name", BulkColumnRole.Regular),
            ("Price", "Price", BulkColumnRole.Regular)
        ]
    };

    private static CachedEntityMetadata CreateLogicKeyProductMetadata(ProviderType provider) => new()
    {
        TableName = "BulkLogicKeyProducts",
        Schema = null,
        Provider = provider,
        Columns =
        [
            ("PersistenceId", "PersistenceId", BulkColumnRole.Key),
            ("Sku", "Sku", BulkColumnRole.LogicKey),
            ("Name", "Name", BulkColumnRole.Regular),
            ("Price", "Price", BulkColumnRole.Regular)
        ]
    };

    private static CachedEntityMetadata CreateCustomerMetadata(ProviderType provider) => new()
    {
        TableName = "BulkCustomers",
        Schema = null,
        Provider = provider,
        Columns =
        [
            ("PersistenceId", "PersistenceId", BulkColumnRole.Key),
            ("FullName", "FullName", BulkColumnRole.Regular),
            ("Email", "Email", BulkColumnRole.Regular),
            ("CreatedAt", "CreatedAt", BulkColumnRole.InsertOnly),
            ("CreatedBy", "CreatedBy", BulkColumnRole.InsertOnly),
            ("UpdatedAt", "UpdatedAt", BulkColumnRole.UpdateOnly),
            ("UpdatedBy", "UpdatedBy", BulkColumnRole.UpdateOnly)
        ]
    };

    private static CachedEntityMetadata CreateOrderMetadata(ProviderType provider) => new()
    {
        TableName = "BulkOrders",
        Schema = null,
        Provider = provider,
        Columns =
        [
            ("PersistenceId", "PersistenceId", BulkColumnRole.Key),
            ("OrderNumber", "OrderNumber", BulkColumnRole.Regular),
            ("Total", "Total", BulkColumnRole.Regular),
            ("IsDeleted", "IsDeleted", BulkColumnRole.SoftDelete),
            ("DeletedAt", "DeletedAt", BulkColumnRole.SoftDelete),
            ("DeletedBy", "DeletedBy", BulkColumnRole.SoftDelete)
        ]
    };

    #endregion
}
