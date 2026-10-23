using System.Data.Common;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>
///     Sessions: an open connection and its dialect, holding the commands the plans prepared on it. A session per request on the
///     provider's pool prepares every command again (the prepared statement belongs to the connection wrapper the pool hands out);
///     the library's SqlSessionPool lends open sessions with their prepared commands to each request instead. ConfigureCommand
///     runs on every command the session creates, for provider settings the library does not know.
/// </summary>
internal static class Sessions
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed, Func<DbConnection> connect)
    {
        var checks = new Checks(session, "sessions");
        var orderId = seed.Orders[4321].Id;

        // The pool: a session rented, used, returned with its prepared commands; rented again it is the same object, so the plan finds its command.
        await using var pool = new SqlSessionPool(connect, session.Dialect, capacity: 4);
        SqlSession first;
        using (var lease = await pool.RentAsync())
        {
            first = lease.Session;
            _ = await first.Query<Order>().FirstOrDefaultAsync((o, id) => o.Id == id, orderId);
        }

        SqlSession second;
        using (var lease = await pool.RentAsync())
        {
            second = lease.Session;
            _ = await second.Query<Order>().FirstOrDefaultAsync((o, id) => o.Id == id, orderId);
        }

        checks.Check("session pool reuses the session", ReferenceEquals(first, second) && pool.Idle == 1);

        // A session returned inside a transaction is not kept: the next rent opens a new one.
        using (var lease = await pool.RentAsync())
            lease.Session.BeginTransaction().Dispose();
        checks.Check("a session in a transaction is not kept", pool.Idle == 1 || pool.Idle == 0);

        // Transactions: an isolation level, a savepoint, rows locked for the transaction (ForUpdate: FOR UPDATE / UPDLOCK; SkipLocked is a work queue, another
        // worker takes the rows this one does not hold; SQLite locks the database as a whole, so the other worker sees the same rows there), and a unit of
        // work in its own transaction, run again when the database reports a transient failure (a serialization failure, a deadlock).
        // Ordered by the key: SQL Server locks every row it reads to order a TOP, so an order on a column without an index would lock them all. The queue runs
        // under READ COMMITTED: SQL Server allows READPAST (SkipLocked) there and under REPEATABLE READ only; the serializable level is TransactAsync's below.
        var queue = Guid.NewGuid();
        await session.InsertManyAsync([Job(queue, "j1"), Job(queue, "j2"), Job(queue, "j3"), Job(queue, "j4")]);
        var worker = new SqlSession(connect(), session.Dialect);
        await worker.Connection.OpenAsync();
        List<Guid> mine, theirs;
        using (worker)
        using (var holding = session.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
        {
            mine = await session.Query<Event>().Where((e, id) => e.OrderId == id, queue).OrderBy(e => e.Id).Take(2).ForUpdate(SqlLockMode.SkipLocked).Select(e => e.Id).ToListAsync();
            theirs = await worker.Query<Event>().Where((e, id) => e.OrderId == id, queue).OrderBy(e => e.Id).Take(2).ForUpdate(SqlLockMode.SkipLocked).Select(e => e.Id).ToListAsync();
            holding.Save("point");
            await session.InsertAsync(Job(queue, "undone"));
            holding.Rollback("point");
            holding.Commit();
        }

        var attempts = 0;
        var total = await session.TransactAsync(async (s, ct) =>
        {
            attempts++;
            await s.InsertAsync(Job(queue, "j5"), ct);
            return await s.Query<Event>().CountAsync((e, id) => e.OrderId == id, queue, ct);
        }, new SqlTransactionOptions { Isolation = System.Data.IsolationLevel.Serializable, Attempts = 3 });
        var skipped = session.Dialect == SqlDialect.Sqlite ? theirs.SequenceEqual(mine) : !mine.Intersect(theirs).Any();
        checks.Check("transactions: isolation level, savepoint, ForUpdate SkipLocked as a work queue, TransactAsync", mine.Count == 2 && theirs.Count == 2 && skipped && total == 5 && attempts == 1);

        // Warmup: every plan of the loaded assemblies and every entity's base writes get their command prepared on the session ahead of the first call
        // (no PREPARE round trip then); a statement the database cannot prepare (a function the application did not register) is reported, not fatal.
        var fresh = new SqlSession(connect(), session.Dialect);
        await fresh.Connection.OpenAsync();
        using (fresh)
        {
            var warmed = await fresh.WarmupAsync();
            var again = await fresh.WarmupAsync();
            var count = await fresh.Query<Order>().CountAsync((o, id) => o.CustomerId == id, orderId == Guid.Empty ? Guid.Empty : seed.Orders[0].CustomerId);
            checks.Check("WarmupAsync prepares every plan once", warmed.Prepared > 100 && again.Prepared == 0 && count == seed.Orders.Count(o => o.CustomerId == seed.Orders[0].CustomerId) && warmed.Skipped.All(s => s.Error.Message.Contains("REGEXP", StringComparison.Ordinal) || s.Error.Message.Contains("orders_of", StringComparison.Ordinal)));
        }

        // Origin and metrics: every plan knows its call site; a session with AnnotateStatements writes it as a comment before the statement (the database's
        // log then names the call site); the meter "Pragmatic.Persistence.Sql" counts and times executions per plan, the activity source traces them.
        var executions = 0L;
        using (var meter = new System.Diagnostics.Metrics.MeterListener())
        {
            meter.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == SqlTelemetry.Name && instrument.Name == "pragmatic.sql.executions") listener.EnableMeasurementEvents(instrument); };
            meter.SetMeasurementEventCallback<long>((_, value, _, _) => executions += value);
            meter.Start();
            var annotating = new SqlSession(connect(), session.Dialect) { AnnotateStatements = true };
            await annotating.Connection.OpenAsync();
            using (annotating)
            {
                using var annotated = annotating.Query<Order>().Where((o, id) => o.Id == id, orderId).ToCommand();
                var one = await annotating.Query<Order>().CountAsync((o, id) => o.Id == id, orderId);
                checks.Check("origin as a statement comment, executions counted per plan", annotated.CommandText.StartsWith("/* Sessions.cs(", StringComparison.Ordinal) && annotated.CommandText.Contains("*/ SELECT ", StringComparison.Ordinal) && one == 1 && executions >= 1);
            }
        }

        // Schema drift: the generated model against the database's catalog (information_schema / PRAGMA table_info): missing tables and columns, incompatible
        // types, unmapped columns, each named with what the model expects. A startup check or a deployment gate.
        var schema = await session.VerifySchemaAsync();
        checks.Check("VerifySchemaAsync: the model matches the database", schema.IsCompatible && schema.Tables >= 20);

        // ConfigureCommand: every command the session creates passes through it (here: a command timeout).
        var configured = 0;
        var connection = connect();
        await connection.OpenAsync();
        using (var tuned = new SqlSession(connection, session.Dialect) { ConfigureCommand = command => { command.CommandTimeout = 30; configured++; } })
        {
            _ = await tuned.Query<Order>().FirstOrDefaultAsync((o, id) => o.Id == id, orderId);
            _ = await tuned.Query<Order>().CountAsync();
        }

        checks.Check("ConfigureCommand on every command", configured == 2);
        return checks.Failures;
    }

    private static Event Job(Guid queue, string kind) => new() { Id = Guid.NewGuid(), OrderId = queue, Kind = kind, At = DateTimeOffset.UtcNow };
}
