using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.DiagnosticsFixture;

/// <summary>A [SoftDelete] entity: a set-based delete would remove its rows.</summary>
[Entity]
[SoftDelete]
public sealed class Archive
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>PRAG3003: an [Auditable] column the writer cannot fill (the user may be unknown: string?).</summary>
[Entity]
[Auditable]
public sealed class StrictAudit
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>PRAG3001: ExecuteDeleteAsync on a [SoftDelete] entity; IncludeDeleted() on an entity that does not soft delete.</summary>
public static class SoftDeletes
{
    public static async Task Run(SqlSession session, Guid id)
    {
        await session.Query<Archive>().Where((a, key) => a.Id == key, id).ExecuteDeleteAsync();
        await session.Query<Order>().IncludeDeleted().ToListAsync();
    }
}

/// <summary>PRAG3001: a query expression over a SqlQuery, which binds to the operators with no invocation to intercept.</summary>
public static class QuerySyntax
{
    public static Task<List<string>> Run(SqlSession session)
        => (from o in session.Query<Order>()
            join c in session.Query<Customer>() on o.CustomerId equals c.Id
            select c.Name).ToListAsync().AsTask();
}
