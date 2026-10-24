namespace Pragmatic.Persistence.Sql;

/// <summary>What a save does with a tracked entity, as EF Core's entry states name it.</summary>
public enum SqlEntryState
{
    Unchanged,
    Added,
    Modified,
    Deleted,
}
