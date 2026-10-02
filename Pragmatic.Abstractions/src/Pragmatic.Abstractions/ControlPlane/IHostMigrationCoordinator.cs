namespace Pragmatic.ControlPlane;

/// <summary>
///     Runs the host's database migrations on demand — the same per-database work the host does at
///     startup, invokable later via a <c>MigrateCommand</c>. The implementation is SG-generated in the
///     host assembly (it knows the databases, their schema metadata, and their config keys at compile
///     time); it delegates to the Migrations runner, whose DB advisory lock already ensures a single
///     node migrates each database (so a non-leader call is a safe wait/no-op).
/// </summary>
/// <remarks>
///     Present only when <c>Pragmatic.Migrations</c> is referenced and the host declares databases.
///     Absent otherwise — a <c>MigrateCommand</c> then logs that no coordinator is available.
/// </remarks>
public interface IHostMigrationCoordinator
{
    /// <summary>Migrates every database this host owns. Throws if a migration fails (fail-fast).</summary>
    Task MigrateAllAsync(CancellationToken ct = default);
}
