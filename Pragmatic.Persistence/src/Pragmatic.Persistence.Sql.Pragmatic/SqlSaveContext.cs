namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>A save in progress, as a phase sees it: the entities written (unchanged ones left out), when, by whom, and the batch they go in.</summary>
public sealed class SqlSaveContext
{
    internal SqlSaveContext(IReadOnlyList<SqlEntry> entries, SqlWriteBatch batch, DateTimeOffset now, string? userId)
    {
        Entries = entries;
        Batch = batch;
        Now = now;
        UserId = userId;
    }

    /// <summary>The entities added, modified or deleted, in the order they were tracked.</summary>
    public IReadOnlyList<SqlEntry> Entries { get; }

    /// <summary>The save's statements: what a phase adds here is written with the entities (<see cref="SqlWriteBatch.InsertAsIs" />).</summary>
    public SqlWriteBatch Batch { get; }

    public DateTimeOffset Now { get; }

    /// <summary>The caller, or null when there is no one to attribute the save to (a job, an anonymous caller).</summary>
    public string? UserId { get; }
}
