namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Property-level change tracking an entity carries itself: the repository's contract
///     (<c>Pragmatic.Persistence.Entity.IChangeTracking</c>, implemented by its source generator, whose <c>Set{Property}</c>
///     methods record the names of the properties they changed). <c>SaveAsync</c> reads it: <see cref="IsNew" /> is an insert,
///     <see cref="ModifiedProperties" /> an UPDATE of those columns only, then <see cref="ResetModifiedProperties" />. This is
///     its stand-in in the spike: same members, so the repository's entities fit as they are.
/// </summary>
public interface IChangeTracking
{
    /// <summary>The names of the properties changed since the last reset (an owned type's name when it was replaced).</summary>
    IReadOnlySet<string> ModifiedProperties { get; }

    /// <summary>The names of the collection navigations changed since the last reset (read by the graph save, not by a row's).</summary>
    IReadOnlySet<string> CollectionsModified { get; }

    /// <summary>Clears both sets: the row is what the database has.</summary>
    void ResetModifiedProperties();

    /// <summary>True until the first save: the row is not in the database yet, so the save inserts it.</summary>
    bool IsNew { get; set; }
}
