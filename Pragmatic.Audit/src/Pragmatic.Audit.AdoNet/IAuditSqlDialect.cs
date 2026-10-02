namespace Pragmatic.Audit.AdoNet;

/// <summary>
///     The two statements the ADO.NET writer needs, in a form each database accepts.
/// </summary>
/// <remarks>
///     Only the segment upsert genuinely differs between providers — <c>ON CONFLICT</c>,
///     <c>MERGE</c>, <c>INSERT OR IGNORE</c> — but it has to be right, because two writers opening the
///     same segment at the same instant is the normal case, not an edge one. The insert is here as well
///     so a provider that quotes identifiers differently has one place to say so.
/// </remarks>
public interface IAuditSqlDialect
{
    /// <summary>Inserts one entry. Parameters: see <see cref="AdoNetAuditTrail" />.</summary>
    string InsertEntry { get; }

    /// <summary>
    ///     Creates the segment row if it is not already there, and does nothing if it is.
    /// </summary>
    /// <remarks>
    ///     Must not fail on a concurrent insert of the same segment. A writer that treats "someone else
    ///     created it first" as an error turns ordinary contention into failed audit writes.
    /// </remarks>
    string UpsertSegment { get; }

    /// <summary>Reads whether a segment exists and is sealed. Returns zero rows when absent.</summary>
    string SelectSegmentSealed { get; }

    /// <summary>
    ///     Creates the trail's tables if they are absent, for a producer that provisions its own schema.
    /// </summary>
    /// <remarks>
    ///     Kept here, beside the statements that read and write those tables, rather than in each
    ///     consumer. A column added to the DDL in one package and to the INSERT in another is exactly
    ///     the drift that shows up as a runtime error long after the change. Consumers that manage the
    ///     schema with EF migrations do not need this.
    /// </remarks>
    string CreateSchema { get; }
}
