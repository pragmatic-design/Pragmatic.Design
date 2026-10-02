using System.Data;
using System.Data.Common;

namespace Pragmatic.Migrations.Tests.Provider;

/// <summary>
///     Counts the commands executed on a borrowed connection, so a test can assert how many
///     round-trips an operation costs. Introspection that issues commands per table makes the
///     cost of starting a host grow with the size of its schema.
/// </summary>
internal sealed class CountingConnection(DbConnection inner) : DbConnection
{
    private int _executions;

    /// <summary>Number of commands executed since the last <see cref="Reset"/>.</summary>
    public int Executions => Volatile.Read(ref _executions);

    public void Reset() => Volatile.Write(ref _executions, 0);

    internal void CountExecution() => Interlocked.Increment(ref _executions);

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

    protected override DbCommand CreateDbCommand() => new CountingCommand(inner.CreateCommand(), this);

    /// <summary>Delegates everything to the real command, tallying each execution on the way.</summary>
    private sealed class CountingCommand(DbCommand inner, CountingConnection owner) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }

        protected override DbConnection? DbConnection { get => owner; set { /* bound to the borrowed connection */ } }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        protected override DbTransaction? DbTransaction { get => inner.Transaction; set => inner.Transaction = value; }

        public override void Cancel() => inner.Cancel();
        public override void Prepare() => inner.Prepare();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();

        public override int ExecuteNonQuery()
        {
            owner.CountExecution();
            return inner.ExecuteNonQuery();
        }

        public override object? ExecuteScalar()
        {
            owner.CountExecution();
            return inner.ExecuteScalar();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            owner.CountExecution();
            return inner.ExecuteReader(behavior);
        }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior, CancellationToken cancellationToken)
        {
            owner.CountExecution();
            return await inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            owner.CountExecution();
            return inner.ExecuteNonQueryAsync(cancellationToken);
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            owner.CountExecution();
            return inner.ExecuteScalarAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
