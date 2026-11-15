namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A phase of a <see cref="SqlUnitOfWork" />'s save registered by a module, run after the unit of work's own phases (audit, soft delete,
///     owner, scope, tenant, lifecycle events), in the order registered: where Pragmatic's audit log and outboxes hang on the Sql engine, as
///     their interceptors run after the others on EF Core. A phase reads the entities being saved and may add rows to the save's batch: they
///     are written in the same command and transaction as the entities.
/// </summary>
public interface ISqlSavePhase
{
    ValueTask ApplyAsync(SqlSaveContext save, CancellationToken ct);

    /// <summary>
    ///     Whether the phase has rows to write even when the save writes no entity. A save with nothing to write returns before its phases,
    ///     unless one of them answers true: then only the phases run, in one batch (#803). An outbox answers true while an operation's events
    ///     are staged; a phase that only follows the entities keeps the default.
    /// </summary>
    bool HasPendingWork => false;
}
