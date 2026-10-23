using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.DiagnosticsFixture;

/// <summary>PRAG3001: a set-based command on rows that feed [RollUp] aggregates (CartLine feeds Cart's) would leave them stale.</summary>
public static class RollUps
{
    public static async Task Run(SqlSession session, Guid cart)
        => await session.Query<CartLine>().Where((l, id) => l.CartId == id, cart).ExecuteDeleteAsync();
}
