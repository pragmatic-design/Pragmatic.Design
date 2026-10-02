using System.Data.Common;
using Pragmatic.Migrations.Diff.Changes;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Context passed to <see cref="IMigrationHook" /> for a single schema change.
///     Carries the open connection AND the active migration transaction so hooks can
///     perform data migration that is atomic with the schema change.
/// </summary>
/// <param name="Connection">The open database connection running the migration.</param>
/// <param name="Transaction">
///     The active migration transaction. Commands created for data migration MUST have
///     their <c>Transaction</c> property set to this value — providers such as SQL Server
///     and SQLite throw when a command runs while a local transaction is pending and the
///     command's transaction is not set.
/// </param>
/// <param name="Change">The schema change being applied.</param>
public sealed record MigrationStepContext(
    DbConnection Connection,
    DbTransaction Transaction,
    SchemaChange Change);
