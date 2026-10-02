using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pragmatic.Persistence.EFCore.Query;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Query;

/// <summary>
///     The timeline CTE must delimit identifiers per provider (SQL Server brackets are a syntax error on
///     PostgreSQL/SQLite) and partition by parent. These verify the SQL is provider-aware and
///     partitions when a parent key is supplied.
/// </summary>
public class TimelineCteSqlTests
{
    private sealed class ProbeContext(DbContextOptions options) : DbContext(options);

    private static DatabaseFacade Facade(System.Action<DbContextOptionsBuilder> configure)
    {
        var builder = new DbContextOptionsBuilder();
        configure(builder);
        return new ProbeContext(builder.Options).Database;
    }

    [Fact]
    public void Build_SqlServer_UsesBracketDelimiters_AndPartitions()
    {
        var sql = TimelineCteSql.Build(
            Facade(o => o.UseSqlServer("Server=.;Database=x;Trusted_Connection=True;")),
            "StaffAssignment", "ValidFrom", "ValidTo", "PropertyId");

        sql.Should().Contain("[StaffAssignment]");
        sql.Should().Contain("PARTITION BY [PropertyId] ORDER BY [ValidFrom]");
        sql.Should().NotContain("\"StaffAssignment\"");
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("npgsql")]
    public void Build_NonSqlServer_UsesDoubleQuoteDelimiters(string provider)
    {
        var sql = TimelineCteSql.Build(
            Facade(o =>
            {
                if (provider == "sqlite")
                    o.UseSqlite("DataSource=:memory:");
                else
                    o.UseNpgsql("Host=localhost;Database=x;Username=u;Password=p");
            }),
            "StaffAssignment", "valid_from", "valid_to", null);

        // SQL-Server-only brackets are a syntax error on these providers.
        sql.Should().Contain("\"StaffAssignment\"");
        sql.Should().NotContain("[StaffAssignment]");
        sql.Should().Contain("ORDER BY \"valid_from\"");
    }

    [Fact]
    public void Build_WithoutPartitionColumn_EmitsNoPartitionBy()
    {
        var sql = TimelineCteSql.Build(
            Facade(o => o.UseSqlite("DataSource=:memory:")),
            "GlobalTimeline", "ValidFrom", "ValidTo", null);

        sql.Should().NotContain("PARTITION BY");
        sql.Should().Contain("OVER (ORDER BY \"ValidFrom\")");
    }

    [Fact]
    public void Build_ProjectsExactlyTheTimelineEntryColumns()
    {
        // SqlQueryRaw<{Entity}TimelineEntry> binds by column name; project the 4 record columns only.
        var sql = TimelineCteSql.Build(
            Facade(o => o.UseSqlite("DataSource=:memory:")),
            "T", "ValidFrom", "ValidTo", null);

        sql.Should().Contain("SELECT ValidFrom, ValidTo, PreviousValidTo, NextValidFrom FROM Timeline");
    }

    [Fact]
    public void Build_UnsupportedProvider_Throws()
    {
        var act = () => TimelineCteSql.Build(
            Facade(o => o.UseInMemoryDatabase("x")),
            "T", "ValidFrom", "ValidTo", null);

        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*Unsupported database provider*");
    }
}
