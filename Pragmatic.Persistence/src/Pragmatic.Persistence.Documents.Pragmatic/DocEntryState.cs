namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>What a save does with a tracked document, as EF Core's entry states name it.</summary>
public enum DocEntryState
{
    Unchanged,
    Added,
    Modified,
    Deleted,
}
