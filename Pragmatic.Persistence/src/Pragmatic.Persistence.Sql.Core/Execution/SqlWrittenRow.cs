namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>A row a session writes (<see cref="SqlCommitHooks" />), and how.</summary>
/// <param name="Row">The entity, as the caller holds it.</param>
/// <param name="Kind">The write.</param>
public readonly record struct SqlWrittenRow(object Row, SqlWriteKind Kind);
