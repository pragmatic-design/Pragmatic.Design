namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Sees the rows a boundary writes, before they are written and once they are committed, on every engine: EF Core, Sql and documents.
///     Registered in DI (as many as needed); a host with none pays nothing.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Writing" /> runs before the statement that writes the entries: throwing from it refuses the write, and nothing is
///         written. <see cref="Committed" /> runs after the commit that carried them succeeded and before the caller sees the commit's
///         result: throwing from it is what a lost acknowledgement looks like to the caller, a commit that happened and answered an error.
///     </para>
///     <para>
///         A write outside a transaction is its own commit. A write inside one is reported when that transaction commits, and never when it
///         rolls back. The entries are entities, never statements: a set-based write (a bulk update or delete by a filter) names no entity and
///         is not reported.
///     </para>
/// </remarks>
public interface ICommitObserver
{
    /// <summary>Before the entries are written. Throwing refuses the write.</summary>
    void Writing(IReadOnlyList<CommitEntry> entries);

    /// <summary>After the commit that carried the entries succeeded, before its caller sees it.</summary>
    void Committed(IReadOnlyList<CommitEntry> entries);
}
