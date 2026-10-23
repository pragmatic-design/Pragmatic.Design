using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A pool of open sessions, each keeping its connection and its prepared commands: what a session per request
///     costs over a long-lived one (the command prepared again on every call) goes away, without the request
///     holding a connection for longer than it runs. The provider's pool keeps the physical connection but hands
///     out a new wrapper on every open, and a prepared command belongs to the wrapper; this pool keeps the
///     wrapper. A session returned with a transaction still open, or with its connection no longer open, is
///     disposed instead of kept; a session rented when the pool is empty is created and opened.
///     With tenants (<see cref="SqlSessionPool(Func{string, DbConnection}, Func{string, string?}, SqlDialect, int, Action{DbCommand}?, Func{SqlSession, string, CancellationToken, Task}?, SqlExecutionHooks?)" />)
///     each tenant has its connection factory's connection and its schema, and its own idle sessions: a session prepared on one
///     tenant's schema never serves another.
/// </summary>
public sealed class SqlSessionPool : IAsyncDisposable
{
    private const string NoTenant = "";
    private readonly Func<string, DbConnection> _connect;
    private readonly Func<string, string?> _schemaOf;
    private readonly Func<SqlSession, string, CancellationToken, Task>? _prepare;
    private readonly SqlDialect _dialect;
    private readonly int _capacity;
    private readonly Action<DbCommand>? _configureCommand;
    private readonly ConcurrentDictionary<string, ConcurrentBag<SqlSession>> _idle = new(StringComparer.Ordinal);
    private int _kept;

    /// <summary>Creates the pool: <paramref name="connect" /> makes a closed connection the pool opens; <paramref name="capacity" /> idle sessions are kept, one returned beyond that is disposed; <paramref name="configureCommand" /> is handed to every session.</summary>
    public SqlSessionPool(Func<DbConnection> connect, SqlDialect dialect, int capacity = 32, Action<DbCommand>? configureCommand = null, SqlExecutionHooks? hooks = null)
        : this(_ => connect(), _ => null, dialect, capacity, configureCommand, hooks: hooks)
    {
    }

    /// <summary>
    ///     A pool routing tenants: <paramref name="connect" /> makes the closed connection of a tenant (one database per tenant, or the same for
    ///     all), <paramref name="schemaOf" /> names the schema its tables live in (null: the connection's default), <paramref name="prepare" /> runs
    ///     once on every new session before it is handed out (a <c>SET search_path</c>, an <c>ATTACH DATABASE</c> on SQLite, a role).
    /// </summary>
    public SqlSessionPool(Func<string, DbConnection> connect, Func<string, string?> schemaOf, SqlDialect dialect, int capacity = 32, Action<DbCommand>? configureCommand = null, Func<SqlSession, string, CancellationToken, Task>? prepare = null, SqlExecutionHooks? hooks = null)
    {
        _connect = connect;
        _schemaOf = schemaOf;
        _prepare = prepare;
        _dialect = dialect;
        _capacity = capacity;
        _configureCommand = configureCommand;
        _hooks = hooks;
    }

    private readonly SqlExecutionHooks? _hooks; // handed to every session the pool opens

    /// <summary>Idle sessions kept at the moment, every tenant's together.</summary>
    public int Idle => _kept;

    /// <summary>An open session for one request: an idle one, or a new one. Dispose the lease to give it back.</summary>
    public ValueTask<SqlSessionLease> RentAsync(CancellationToken ct = default) => RentAsync(NoTenant, ct);

    /// <summary>An open session on <paramref name="tenant" />'s connection and schema: one of its idle sessions, or a new one. Dispose the lease to give it back.</summary>
    public async ValueTask<SqlSessionLease> RentAsync(string tenant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var idle = _idle.GetOrAdd(tenant, static _ => []);
        while (idle.TryTake(out var kept))
        {
            Interlocked.Decrement(ref _kept);
            if (kept.Connection.State == ConnectionState.Open)
                return new SqlSessionLease(this, kept);
            kept.Dispose();
        }

        var connection = _connect(tenant);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var session = new SqlSession(connection, _dialect) { ConfigureCommand = _configureCommand, Hooks = _hooks, Schema = _schemaOf(tenant), Tenant = tenant == NoTenant ? null : tenant };
        try
        {
            // The dialect's own session settings first (Oracle's binary sort and UTC, MySQL's sql_mode and UTC): the pool opened the connection, so it sets it up.
            if (_dialect == SqlDialect.Oracle)
                await SqlOracle.PrepareAsync(connection, ct).ConfigureAwait(false);
            else if (_dialect == SqlDialect.MySql)
                await SqlMySql.PrepareAsync(connection, ct).ConfigureAwait(false);
            if (_prepare is not null)
                await _prepare(session, tenant, ct).ConfigureAwait(false);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return new SqlSessionLease(this, session);
    }

    internal void Return(SqlSession session)
    {
        if (session.Transaction is not null || session.Connection.State != ConnectionState.Open || Interlocked.Increment(ref _kept) > _capacity)
        {
            if (session.Transaction is null && session.Connection.State == ConnectionState.Open)
                Interlocked.Decrement(ref _kept);
            session.Dispose();
            return;
        }

        _idle.GetOrAdd(session.Tenant ?? NoTenant, static _ => []).Add(session);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var idle in _idle.Values)
        {
            while (idle.TryTake(out var session))
                await session.DisposeAsync().ConfigureAwait(false);
        }

        _kept = 0;
    }
}

/// <summary>A session rented from a <see cref="SqlSessionPool" />: disposing it returns the session (or disposes it, when it cannot be reused).</summary>
public readonly struct SqlSessionLease(SqlSessionPool pool, SqlSession session) : IDisposable
{
    public SqlSession Session => session;

    public void Dispose() => pool.Return(session);
}
