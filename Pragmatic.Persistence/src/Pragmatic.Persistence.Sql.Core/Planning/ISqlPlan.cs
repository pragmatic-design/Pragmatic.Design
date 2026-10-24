namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>A plan seen without its row type: what a warmup needs of every plan of every translated assembly.</summary>
public interface ISqlPlan
{
    /// <summary>The slot of the plan's command in every session's cache.</summary>
    int Id { get; }

    /// <summary>True when the command can be prepared ahead: no IN list the text expands per call, no parameter of unknown kind.</summary>
    bool Preparable { get; }

    /// <summary>False where the plan has no statement for the dialect (MySQL: RETURNING/OUTPUT, a table-valued function).</summary>
    bool Supports(SqlDialect dialect);

    /// <summary>The statement for a dialect (see <see cref="SqlPlan{T}.Sql(SqlDialect)" />).</summary>
    string Sql(SqlDialect dialect);

    /// <summary>Creates and prepares the plan's command on the session when it has none yet; true when it did.</summary>
    Task<bool> WarmAsync(SqlSession session, CancellationToken ct);
}
