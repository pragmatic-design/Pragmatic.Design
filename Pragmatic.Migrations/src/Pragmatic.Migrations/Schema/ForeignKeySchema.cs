namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Foreign key constraint definition.
/// </summary>
/// <param name="Name">Constraint name (e.g. "FK_Reservations_GuestId_Guests").</param>
/// <param name="Column">Source column name.</param>
/// <param name="ReferencedTable">Target table name.</param>
/// <param name="ReferencedColumn">Target column name (typically "PersistenceId").</param>
/// <param name="OnDelete">Delete behavior applied to the ON DELETE clause.</param>
public sealed record ForeignKeySchema(
    string Name,
    string Column,
    string ReferencedTable,
    string ReferencedColumn,
    ReferentialAction OnDelete);
