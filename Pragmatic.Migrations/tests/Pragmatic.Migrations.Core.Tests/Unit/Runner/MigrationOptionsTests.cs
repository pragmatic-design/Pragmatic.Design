#pragma warning disable CA2007

using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Core.Tests.Unit.Runner;

public class MigrationOptionsTests
{
    [Fact]
    public void ShouldMigrate_NullFilter_ReturnsTrue()
    {
        var options = new MigrationOptions();
        options.ShouldMigrate("AnyDatabase").Should().BeTrue();
    }

    [Fact]
    public void ShouldMigrate_EmptyFilter_ReturnsTrue()
    {
        var options = new MigrationOptions { DatabaseFilter = [] };
        options.ShouldMigrate("AnyDatabase").Should().BeTrue();
    }

    [Fact]
    public void ShouldMigrate_MatchingFilter_ReturnsTrue()
    {
        var options = new MigrationOptions { DatabaseFilter = ["AppDb"] };
        options.ShouldMigrate("AppDb").Should().BeTrue();
    }

    [Fact]
    public void ShouldMigrate_NonMatchingFilter_ReturnsFalse()
    {
        var options = new MigrationOptions { DatabaseFilter = ["AppDb"] };
        options.ShouldMigrate("OtherDb").Should().BeFalse();
    }

    [Fact]
    public void ShouldMigrate_NullDatabaseName_ReturnsFalse()
    {
        var options = new MigrationOptions { DatabaseFilter = ["AppDb"] };
        options.ShouldMigrate(null).Should().BeFalse();
    }

    [Fact]
    public void AuditTableName_DefaultIsPragmaticSchema()
    {
        var options = new MigrationOptions();
        options.AuditTableName.Should().Be(MigrationConstants.AuditTableName);
    }

    [Fact]
    public void AuditTableName_CanBeCustomized()
    {
        var options = new MigrationOptions { AuditTableName = "__CustomHistory" };
        options.AuditTableName.Should().Be("__CustomHistory");
    }

    [Fact]
    public void Timeout_DefaultIs30Minutes()
    {
        var options = new MigrationOptions();
        options.Timeout.Should().Be(TimeSpan.FromMinutes(30));
    }
}
