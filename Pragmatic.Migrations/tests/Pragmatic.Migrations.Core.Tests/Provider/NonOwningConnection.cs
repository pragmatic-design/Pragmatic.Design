using System.Data;
using System.Data.Common;

namespace Pragmatic.Migrations.Core.Tests.Provider;

/// <summary>
///     Hands the migration runner a connection it may open, close and dispose freely while the
///     test keeps the real one alive. Needed because the scenario tests assert against the same
///     connection afterwards — and for SQLite <c>:memory:</c> the database itself dies with the
///     connection, so letting the runner dispose it would drop the schema under test.
/// </summary>
internal sealed class NonOwningConnection(DbConnection inner) : DbConnection
{
    internal DbConnection Inner => inner;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => inner.ConnectionString;
        set { /* the inner connection is already configured and open */ }
    }

    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;

    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);

    // Open/Close/Dispose are deliberately inert: the test owns the connection lifetime.
    public override void Open() { }
    public override void Close() { }
    protected override void Dispose(bool disposing) { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() => inner.CreateCommand();
}
