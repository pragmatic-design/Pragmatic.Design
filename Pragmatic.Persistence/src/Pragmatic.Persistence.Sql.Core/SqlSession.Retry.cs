using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>A unit of work in a transaction, run again when the database reports a transient failure.</summary>
public sealed partial class SqlSession
{
    /// <summary>
    ///     Runs <paramref name="work" /> in a transaction of this session (the options' isolation level) and commits it; when the
    ///     database reports a transient failure the transaction is rolled back and the work runs again, up to the options' attempts,
    ///     after the options' delay (doubling). The work sees the rows as the transaction does; what it computed in memory before a
    ///     retry is its own to discard (it runs from the start). The last failure is thrown when the attempts are spent.
    /// </summary>
    public async Task TransactAsync(Func<SqlSession, CancellationToken, Task> work, SqlTransactionOptions? options = null, CancellationToken ct = default)
    {
        await TransactAsync<bool>(async (session, token) =>
        {
            await work(session, token).ConfigureAwait(false);
            return true;
        }, options, ct).ConfigureAwait(false);
    }

    /// <summary>The same, with a result.</summary>
    public async Task<TResult> TransactAsync<TResult>(Func<SqlSession, CancellationToken, Task<TResult>> work, SqlTransactionOptions? options = null, CancellationToken ct = default)
    {
        options ??= SqlTransactionOptions.Default;
        if (Transaction is not null)
            throw new InvalidOperationException("A transaction is already open on this session: TransactAsync starts its own.");
        var delay = options.Delay;
        for (var attempt = 1; ; attempt++)
        {
            using var transaction = BeginTransaction(options.Isolation);
            try
            {
                var result = await work(this, ct).ConfigureAwait(false);
                transaction.Commit();
                return result;
            }
            catch (DbException e) when (attempt < options.Attempts && options.IsTransient(e))
            {
                // Rolled back by the dispose; the next attempt starts a fresh transaction.
            }

            transaction.Dispose();
            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay += delay;
        }
    }
}
