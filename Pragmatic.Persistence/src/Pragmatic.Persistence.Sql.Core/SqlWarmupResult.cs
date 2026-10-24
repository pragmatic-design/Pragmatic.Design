using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>What a warmup did: how many commands it prepared, and the plans the database refused to prepare (their first call reports the same error).</summary>
public sealed class SqlWarmupResult
{
    internal SqlWarmupResult(int prepared, List<(ISqlPlan Plan, DbException Error)> skipped, List<(string Table, DbException Error)> skippedWriters)
    {
        Prepared = prepared;
        Skipped = skipped;
        SkippedWriters = skippedWriters;
    }

    /// <summary>The commands created and prepared: the plans', and the entities' single-row insert, update and delete.</summary>
    public int Prepared { get; }

    /// <summary>The plans whose statement the database would not prepare (a function the application did not register, a table not there): left to their first call.</summary>
    public IReadOnlyList<(ISqlPlan Plan, DbException Error)> Skipped { get; }

    /// <summary>The entities whose writes the database would not prepare (a column type the server lacks, such as a vector without pgvector): left to their first call.</summary>
    public IReadOnlyList<(string Table, DbException Error)> SkippedWriters { get; }
}
