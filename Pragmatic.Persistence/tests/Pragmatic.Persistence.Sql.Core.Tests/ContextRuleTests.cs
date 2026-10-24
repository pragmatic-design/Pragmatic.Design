using System.Linq.Expressions;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.ContextRules;

/// <summary>A row of a tenant, owned by a user, shared through access scopes: the shape of Showcase's guests and invoices.</summary>
[Entity]
[Table("Dossiers")]
[VisibleWhen<OfTheTenant>]
[VisibleWhen<OwnedOrShared>]
public sealed class Dossier
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string[] Scopes { get; set; } = [];
}

/// <summary>The tenant column against the request's tenant (fail closed: no tenant, no row).</summary>
public sealed class OfTheTenant : VisibilityRule<Dossier>
{
    public override Expression<Func<Dossier, bool>> ToExpression() => d => d.TenantId == SqlContext.Current.TenantId;
}

/// <summary>The owner, a holder of one of the row's scopes, or a user with the permission to see every row.</summary>
public sealed class OwnedOrShared : VisibilityRule<Dossier>
{
    public override Expression<Func<Dossier, bool>> ToExpression()
        => d => SqlContext.Current.Has("dossiers.view-all") || d.OwnerId == SqlContext.Current.UserId || d.Scopes.Any(s => SqlContext.Current.Scopes.Contains(s));
}

/// <summary>
///     Visibility rules on the request's context (<see cref="SqlContext" />): the tenant, the owner, the scopes and a permission read again at each
///     execution, so one plan serves every caller; outside a context nothing is visible. Compiled and on the runtime path. In a transaction,
///     rolled back.
/// </summary>
[Collection("databases")]
public class ContextRuleTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Rules_read_the_callers_context_at_each_execution(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "ctx-" + Guid.NewGuid().ToString("N")[..6];
        await session.InsertManyAsync(new[]
        {
            new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "anna", Title = mark + "-anna", Scopes = ["eur"] },
            new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "bruno", Title = mark + "-bruno", Scopes = ["usd"] },
            new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "carla", Title = mark + "-carla", Scopes = ["eur", "gbp"] },
            new Dossier { Id = Guid.NewGuid(), TenantId = "other", OwnerId = "anna", Title = mark + "-elsewhere", Scopes = ["eur"] },
        });

        async Task<string[]> Visible(SqlContext context)
        {
            using (SqlContext.Use(context))
            {
                var compiled = (await session.Query<Dossier>().Where((d, m) => Sql.Like(d.Title, m), mark + "%").ToListAsync()).Select(d => d.Title).Order(StringComparer.Ordinal).ToArray();
                IQueryable<Dossier> runtime = session.Query<Dossier>();
                var composed = (await runtime.Where(d => d.Title.StartsWith(mark)).ToListAsync()).Select(d => d.Title).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(compiled, composed);
                return compiled;
            }
        }

        // The owner sees their rows, plus those shared with a scope they hold; only in their tenant.
        Assert.Equal([mark + "-anna"], await Visible(new SqlContext("acme", "anna")));
        Assert.Equal([mark + "-anna", mark + "-carla"], await Visible(new SqlContext("acme", "dario", ["eur"])));
        Assert.Equal([mark + "-bruno", mark + "-carla"], await Visible(new SqlContext("acme", "nobody", ["usd", "gbp"])));
        // The permission: every row of the tenant, still not another tenant's.
        Assert.Equal([mark + "-anna", mark + "-bruno", mark + "-carla"], await Visible(new SqlContext("acme", "auditor", permissions: ["dossiers.view-all"])));
        Assert.Equal([mark + "-elsewhere"], await Visible(new SqlContext("other", "anna")));
        // Past one rule: the tenant's rows whoever owns them; the tenant rule stays.
        using (SqlContext.Use(new SqlContext("acme", "dario")))
        {
            var tenantRows = await session.Query<Dossier>().WithoutFilter<OwnedOrShared>().Where((d, m) => Sql.Like(d.Title, m), mark + "%").ToListAsync();
            Assert.Equal([mark + "-anna", mark + "-bruno", mark + "-carla"], tenantRows.Select(d => d.Title).Order(StringComparer.Ordinal));
        }

        // No context: nothing (fail closed). Past the rules: everything.
        Assert.Empty(await Visible(SqlContext.Nobody));
        Assert.Equal(4, await session.Query<Dossier>().WithoutFilters().CountAsync((d, m) => Sql.Like(d.Title, m), mark + "%"));
        transaction.Rollback();
    }
}
