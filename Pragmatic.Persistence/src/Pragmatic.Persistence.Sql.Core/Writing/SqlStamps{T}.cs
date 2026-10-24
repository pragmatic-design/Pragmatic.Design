using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     The columns a writer fills itself (the repository's <c>[Auditable]</c> and <c>[SoftDelete]</c>, read by name): who and when, from the clock and
///     <see cref="SqlContext.Current" />'s user. <see cref="Created" /> stamps an insert (CreatedAt/By, then never written again: an update keeps the
///     stored value), <see cref="Updated" /> every write (UpdatedAt/By), <see cref="Deleted" /> a soft delete or a restore (IsDeleted, DeletedAt/By).
///     The setters are generated: no reflection.
/// </summary>
public sealed class SqlStamps<T>(Action<T, DateTimeOffset, string?>? created, Action<T, DateTimeOffset, string?>? updated, Action<T, bool, DateTimeOffset, string?>? deleted, string[] insertOnlyColumns, string[] updatedColumns, string[] deletedColumns)
{
    public Action<T, DateTimeOffset, string?>? Created { get; } = created;

    public Action<T, DateTimeOffset, string?>? Updated { get; } = updated;

    /// <summary>Marks the row deleted (true) or restored (false): IsDeleted, and DeletedAt/By set or cleared.</summary>
    public Action<T, bool, DateTimeOffset, string?>? Deleted { get; } = deleted;

    /// <summary>The columns written by an insert only (CreatedAt/By): an update or an upsert's update keeps what the row holds.</summary>
    public string[] InsertOnlyColumns { get; } = insertOnlyColumns;

    public string[] UpdatedColumns { get; } = updatedColumns;

    /// <summary>The soft delete's columns: IsDeleted first, then DeletedAt and DeletedBy when the entity has them.</summary>
    public string[] DeletedColumns { get; } = deletedColumns;
}
