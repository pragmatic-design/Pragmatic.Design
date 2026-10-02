using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pragmatic.Persistence.EFCore.Query;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Query;

/// <summary>
///     The hierarchy CTE carries a cycle/depth guard (a cyclic parent FK would loop forever on
///     PostgreSQL/SQLite) and quotes the id column in SELECT as it does in WHERE.
/// </summary>
public class HierarchyCteSqlTests
{
    private sealed class ProbeContext(DbContextOptions options) : DbContext(options);

    private static DatabaseFacade Facade(System.Action<DbContextOptionsBuilder> configure)
    {
        var builder = new DbContextOptionsBuilder();
        configure(builder);
        return new ProbeContext(builder.Options).Database;
    }

    [Fact]
    public void Build_IdProjection_QuotesTheColumnAndBoundsDepth()
    {
        // The id column must be quoted in SELECT (raw, a reserved word breaks the query).
        // The explicit projection gets a depth-bounded recursive member.
        var sql = HierarchyCteSql.Build(
            Facade(o => o.UseSqlite("DataSource=:memory:")),
            "OrgUnit", "Order", "ParentId",
            HierarchyCteSql.Direction.Descendants,
            selectColumns: "Order", valueAlias: "Value", maxDepth: 50);

        sql.Should().Contain("SELECT \"Order\", 0 AS __hdepth");
        sql.Should().Contain("WHERE h.__hdepth < 50");
        sql.Should().Contain("\"Order\" AS \"Value\"");
    }

    [Fact]
    public void Build_SqlServer_AppendsMaxRecursionOption()
    {
        var sql = HierarchyCteSql.Build(
            Facade(o => o.UseSqlServer("Server=.;Database=x;Trusted_Connection=True;")),
            "OrgUnit", "Id", "ParentId",
            HierarchyCteSql.Direction.Descendants,
            selectColumns: "Id", maxDepth: 123);

        sql.Should().Contain("OPTION (MAXRECURSION 123)");
        sql.Should().Contain("[OrgUnit]");
    }

    /// <summary>
    ///     "*" callers materialize entities, so the depth column must not reach the result — and the
    ///     bound must hold anyway: the CTE walks keys only, and the rows come from the table by key.
    /// </summary>
    /// <remarks>
    ///     ⚠️ "*" still needs the depth column: the generated hierarchy queries project "*", and on
    ///     PostgreSQL and SQLite a cycle between parents is a recursion with no end without it.
    /// </remarks>
    [Fact]
    public void Build_StarProjection_BoundsDepthInsideTheCte_AndSelectsRowsFromTheTable()
    {
        var sql = HierarchyCteSql.Build(
            Facade(o => o.UseSqlite("DataSource=:memory:")),
            "OrgUnit", "Id", "ParentId",
            HierarchyCteSql.Direction.Descendants,
            excludeRoot: true, maxDepth: 7);

        sql.Should().Contain("SELECT \"Id\", \"ParentId\", 0 AS __hdepth FROM \"OrgUnit\"");
        sql.Should().Contain("WHERE h.__hdepth < 7");
        sql.Should().Contain("SELECT * FROM \"OrgUnit\" WHERE \"Id\" IN (SELECT \"Id\" FROM Hierarchy) AND \"Id\" <> {0}");
        sql.Should().NotContain("SELECT * FROM Hierarchy",
            "the depth column lives in the CTE and the entity rows are read from the table");
    }
}
