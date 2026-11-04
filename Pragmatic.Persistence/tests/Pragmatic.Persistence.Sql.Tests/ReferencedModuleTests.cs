using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.ReferencedModule;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Entities of another module read on the five databases (#502), as a boundary reads them through <c>[ReadAccess&lt;T&gt;]</c>: the
///     suite sees <c>Part</c> and <c>Supplier</c> as metadata, where a private setter (<c>Code</c>, the generated <c>SupplierId</c>) has no
///     setter Roslyn can see, and the rule <c>ListedOnly</c> has no body. The module writes its rows; this assembly filters on the foreign key,
///     projects a field of the navigation, and reads past the suppliers the module's rule hides only when it says so.
/// </summary>
[Collection("pragmatic databases")]
public class ReferencedModuleTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private Task<SqlSession> SessionAsync(SqlDialect dialect)
        => databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Another_modules_entity_is_filtered_on_its_foreign_key_and_projects_its_navigation(SqlDialect dialect)
    {
        using var session = await SessionAsync(dialect);
        var (acme, _) = await ReferencedRows.SeedAsync(session);

        var parts = await session.Query<Part>().Where((p, supplier) => p.SupplierId == supplier, acme).OrderBy(p => p.Code).Select(p => new { p.Code, Supplier = p.Supplier.Name }).ToListAsync();

        Assert.Equal([("A-1", "ACME"), ("A-2", "ACME")], parts.Select(p => (p.Code, p.Supplier)));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Another_modules_entity_is_read_whole_with_its_private_setters_and_its_key(SqlDialect dialect)
    {
        using var session = await SessionAsync(dialect);
        var (_, bolt) = await ReferencedRows.SeedAsync(session);

        var part = await session.Query<Part>().Include(p => p.Supplier).Where((p, supplier) => p.SupplierId == supplier, bolt).SingleAsync();

        Assert.Equal("B-1", part.Code);
        Assert.Equal(bolt, part.SupplierId);
        Assert.Equal(bolt, part.Supplier.Id);
        Assert.Equal("BOLT", part.Supplier.Name);
        Assert.NotEqual(Guid.Empty, part.Id);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Another_modules_rule_hides_its_rows_here_and_through_a_required_navigation(SqlDialect dialect)
    {
        using var session = await SessionAsync(dialect);
        await ReferencedRows.SeedAsync(session);

        var suppliers = await session.Query<Supplier>().OrderBy(s => s.Name).Select(s => s.Name).ToListAsync();
        var parts = await session.Query<Part>().OrderBy(p => p.Code).Select(p => new { p.Code, Supplier = p.Supplier.Name }).ToListAsync();
        var everySupplier = await session.Query<Supplier>().WithoutFilter<ListedOnly>().OrderBy(s => s.Name).Select(s => s.Name).ToListAsync();

        Assert.Equal(["ACME", "BOLT"], suppliers);
        Assert.Equal(["A-1", "A-2", "B-1"], parts.Select(p => p.Code)); // G-1 and E-1 go with their supplier: the join is INNER
        Assert.Equal(["ACME", "BOLT", "EVIL", "GONE"], everySupplier);
    }
}
