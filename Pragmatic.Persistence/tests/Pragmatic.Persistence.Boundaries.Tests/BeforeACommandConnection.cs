using System.Data;
using System.Data.Common;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     A connection over another that runs a step of the test's before the first command whose text contains a given fragment: the test puts
///     its own write between two commands of the code under test, on every run, instead of hoping a concurrent writer lands there. Everything
///     else is the inner connection's; it is borrowed, so disposing this one leaves it open.
/// </summary>
internal sealed class BeforeACommandConnection(DbConnection inner) : DbConnection
{
    private string? _fragment;
    private int _remaining;
    private Action<DbConnection>? _before;

    /// <summary>
    ///     The <paramref name="occurrence" />-th command from now whose text contains <paramref name="fragment" /> runs after
    ///     <paramref name="before" />, given the inner connection.
    /// </summary>
    public void Before(string fragment, int occurrence, Action<DbConnection> before)
    {
        _before = before;
        _remaining = occurrence;
        _fragment = fragment;
    }

    internal void Arrive(string commandText)
    {
        if (_fragment is { } fragment && commandText.Contains(fragment, StringComparison.Ordinal) && --_remaining == 0)
        {
            _fragment = null;
            _before!(inner);
        }
    }

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

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);

    protected override DbCommand CreateDbCommand() => new Command(inner.CreateCommand(), this);

    /// <summary>The inner connection's command, announced to its connection before each execution.</summary>
    private sealed class Command(DbCommand inner, BeforeACommandConnection owner) : DbCommand
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
            owner.Arrive(CommandText);
            return inner.ExecuteNonQuery();
        }

        public override object? ExecuteScalar()
        {
            owner.Arrive(CommandText);
            return inner.ExecuteScalar();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            owner.Arrive(CommandText);
            return inner.ExecuteReader(behavior);
        }

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            owner.Arrive(CommandText);
            return await inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            owner.Arrive(CommandText);
            return inner.ExecuteNonQueryAsync(cancellationToken);
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            owner.Arrive(CommandText);
            return inner.ExecuteScalarAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
