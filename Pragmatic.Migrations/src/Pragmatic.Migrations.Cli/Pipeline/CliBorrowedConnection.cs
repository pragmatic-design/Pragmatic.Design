using System.Data;
using System.Data.Common;

namespace Pragmatic.Migrations.Cli.Pipeline;

/// <summary>
///     Lends the already-open CLI connection to the migration runner without transferring
///     ownership. The runner opens and disposes the connection it is handed; the CLI still needs
///     it afterwards (and holds the advisory lock on that very session), so open/close/dispose are
///     inert here and every command is executed on the borrowed connection.
/// </summary>
internal sealed class CliBorrowedConnection(DbConnection inner) : DbConnection
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => inner.ConnectionString;
        set { /* the borrowed connection is already configured and open */ }
    }

    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;

    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);

    public override void Open() { }
    public override void Close() { }
    protected override void Dispose(bool disposing) { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() => inner.CreateCommand();
}
