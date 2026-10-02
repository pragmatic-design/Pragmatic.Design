using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.Core.Tests.Unit;

public class ConstantsTests
{
    [Fact]
    public void ProviderNames_AreConsistent()
    {
        MigrationConstants.ProviderPostgreSql.Should().Be("PostgreSql");
        MigrationConstants.ProviderSqlServer.Should().Be("SqlServer");
        MigrationConstants.ProviderSqlite.Should().Be("Sqlite");
    }

    [Fact]
    public void AuditTableName_IsDefault()
    {
        MigrationConstants.AuditTableName.Should().Be("__PragmaticSchema");
    }

    [Fact]
    public void EfMigrationsTableName_IsDefault()
    {
        MigrationConstants.EfMigrationsTableName.Should().Be("__EFMigrationsHistory");
    }

    [Fact]
    public void SchemaDefaults_AreCorrect()
    {
        MigrationConstants.PostgreSqlDefaultSchema.Should().Be("public");
        MigrationConstants.SqlServerDefaultSchema.Should().Be("dbo");
    }
}
