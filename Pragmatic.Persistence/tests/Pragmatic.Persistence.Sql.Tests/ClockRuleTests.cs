using System.Linq.Expressions;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The repository's <c>[SoftDelete]</c> and <c>[TemporalRelation]</c> as visibility rules: a deleted row is not there, and only the relation valid
///     now is (the database's clock, <c>Sql.UtcNow()</c>: the rule is a plan, no value of the moment to bind). <c>ActiveAt(instant)</c> is the chain
///     past the rules with the period compared to an argument.
/// </summary>
[Entity]
[Table("Memberships")]
[VisibleWhen<NotDeletedMembership>]
[VisibleWhen<ActiveMembership>]
public sealed class Membership
{
    public Guid Id { get; set; }
    public string Member { get; set; } = "";
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>[SoftDelete]'s filter.</summary>
public sealed class NotDeletedMembership : VisibilityRule<Membership>
{
    public override Expression<Func<Membership, bool>> ToExpression() => m => !m.IsDeleted;
}

/// <summary>[TemporalRelation]'s filter: valid now, by the database's clock (UTC).</summary>
public sealed class ActiveMembership : VisibilityRule<Membership>
{
    public override Expression<Func<Membership, bool>> ToExpression() => m => m.ValidFrom <= Sql.UtcNow() && (m.ValidTo == null || m.ValidTo > Sql.UtcNow());
}

[Collection("databases")]
public class ClockRuleTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_rule_on_the_clock_shows_the_relations_valid_now(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var now = DateTime.UtcNow;
        var group = "m-" + Guid.NewGuid().ToString("N")[..6];
        await session.InsertManyAsync(new[]
        {
            new Membership { Id = Guid.NewGuid(), Member = group + "-active", ValidFrom = now.AddHours(-1) },
            new Membership { Id = Guid.NewGuid(), Member = group + "-closed", ValidFrom = now.AddHours(-3), ValidTo = now.AddHours(-2) },
            new Membership { Id = Guid.NewGuid(), Member = group + "-future", ValidFrom = now.AddHours(2) },
            new Membership { Id = Guid.NewGuid(), Member = group + "-deleted", ValidFrom = now.AddHours(-1), IsDeleted = true },
            new Membership { Id = Guid.NewGuid(), Member = group + "-ending", ValidFrom = now.AddHours(-1), ValidTo = now.AddHours(1) },
        });

        // Compiled and on the runtime path: the rules with the database's clock.
        var visible = await session.Query<Membership>().Where((m, g) => Sql.Like(m.Member, g), group + "%").OrderBy(m => m.Member).ToListAsync();
        Assert.Equal([group + "-active", group + "-ending"], visible.Select(m => m.Member));
        var runtime = await session.Query<Membership>().AsQueryable().Where(m => m.Member.StartsWith(group)).OrderBy(m => m.Member).ToListAsync();
        Assert.Equal(visible.Select(m => m.Id), runtime.Select(m => m.Id));

        // ActiveAt(instant): past the rules, the period compared to the argument (here 2.5 hours ago: the closed one).
        var at = await session.Query<Membership>().WithoutFilters().Where((m, a) => Sql.Like(m.Member, a.Group) && !m.IsDeleted && m.ValidFrom <= a.At && (m.ValidTo == null || m.ValidTo > a.At), (Group: group + "%", At: now.AddHours(-2.5))).ToListAsync();
        Assert.Equal([group + "-closed"], at.Select(m => m.Member));
        Assert.Equal(5, await session.Query<Membership>().WithoutFilters().CountAsync((m, g) => Sql.Like(m.Member, g), group + "%"));
        transaction.Rollback();
    }
}
