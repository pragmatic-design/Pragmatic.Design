using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Warmup: every plan of every translated assembly loaded (<see cref="SqlPlans" />) and every entity's single-row insert, update
///     and delete get their command created and prepared on this session, so the first call of each pays no PREPARE round trip
///     (PostgreSQL and SQL Server prepare on the server; SQLite compiles the statement). For a long-lived session, or the one a
///     <see cref="SqlSessionPool" /> hands out first.
/// </summary>
public sealed partial class SqlSession
{
    /// <summary>Prepares the commands of every registered plan and writer this session has none for yet.</summary>
    public async Task<SqlWarmupResult> WarmupAsync(CancellationToken ct = default)
    {
        var prepared = 0;
        var skipped = new List<(ISqlPlan, DbException)>();
        var skippedWriters = new List<(string, DbException)>();
        foreach (var accessor in SqlPlans.All)
        {
            var plan = accessor();
            try
            {
                if (await plan.WarmAsync(this, ct).ConfigureAwait(false))
                    prepared++;
            }
            catch (DbException error)
            {
                skipped.Add((plan, error)); // this statement is the plan's problem, reported again by its first call; the others still warm
            }
        }

        foreach (var map in SqlEntityMap.All)
        {
            if (map.WriterBoxed is not { } writer)
                continue;
            try
            {
                prepared += writer.Warm(this);
            }
            catch (DbException error)
            {
                skippedWriters.Add((map.Table, error)); // the entity's statements are its problem (a type the server lacks), reported again by the first write; the others still warm
            }
        }

        return new SqlWarmupResult(prepared, skipped, skippedWriters);
    }
}
