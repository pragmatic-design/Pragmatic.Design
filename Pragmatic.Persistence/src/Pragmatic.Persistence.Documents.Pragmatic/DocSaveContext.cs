namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>What a module's phase sees of the save: the entries written, the session, the save's instant and its author.</summary>
public sealed class DocSaveContext(IReadOnlyList<DocEntry> entries, DocSession session, DateTimeOffset now, string? userId)
{
    /// <summary>The entities the save writes, in their states after the unit of work's phases (a soft delete is a modified entity).</summary>
    public IReadOnlyList<DocEntry> Entries { get; } = entries;

    /// <summary>The session the save writes through: what a phase writes is in the save's transaction where there is one.</summary>
    public DocSession Session { get; } = session;

    public DateTimeOffset Now { get; } = now;

    public string? UserId { get; } = userId;
}
