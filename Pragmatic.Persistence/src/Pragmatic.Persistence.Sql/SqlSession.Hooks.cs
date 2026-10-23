using System.Data.Common;
using System.Diagnostics;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The statements that are not a plan's (a writer's, a batch's, a sequence's, an explicit load) run through these: the session's
///     <see cref="Hooks" /> see them as they see the plans. Without hooks each is the provider's call and nothing else.
/// </summary>
public sealed partial class SqlSession
{
    internal async Task<int> RunAsync(DbCommand command, string origin, CancellationToken ct)
    {
        if (Hooks is null)
            return await NonQueryAsync(command, ct).ConfigureAwait(false);
        var started = Begin(command, origin);
        try
        {
            var affected = await NonQueryAsync(command, ct).ConfigureAwait(false);
            End(origin, started, null);
            return affected;
        }
        catch (Exception e)
        {
            End(origin, started, e);
            throw;
        }
    }

    internal async Task<object?> ScalarAsync(DbCommand command, string origin, CancellationToken ct)
    {
        if (Hooks is null)
            return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var started = Begin(command, origin);
        try
        {
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            End(origin, started, null);
            return value;
        }
        catch (Exception e)
        {
            End(origin, started, e);
            throw;
        }
    }

    /// <summary>The reader of a command; the hooks' time is the command's start (the rows are the caller's to read).</summary>
    internal async Task<DbDataReader> ReadAsync(DbCommand command, string origin, CancellationToken ct)
    {
        if (Hooks is null)
            return await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var started = Begin(command, origin);
        try
        {
            var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            End(origin, started, null);
            return reader;
        }
        catch (Exception e)
        {
            End(origin, started, e);
            throw;
        }
    }

    /// <summary>The reader of a provider batch (pipelining): the hooks see no command.</summary>
    internal async Task<DbDataReader> ReadAsync(DbBatch batch, string origin, CancellationToken ct)
    {
        if (Hooks is null)
            return await batch.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var started = Begin(null, origin);
        try
        {
            var reader = await batch.ExecuteReaderAsync(ct).ConfigureAwait(false);
            End(origin, started, null);
            return reader;
        }
        catch (Exception e)
        {
            End(origin, started, e);
            throw;
        }
    }

    /// <summary>The rows a command touched: the provider's count, or on Oracle a block's own (an anonymous block reports none).</summary>
    private Task<int> NonQueryAsync(DbCommand command, CancellationToken ct)
        => Dialect == SqlDialect.Oracle && SqlOracle.IsBlock(command.CommandText) ? SqlOracle.ExecuteCountAsync(command, ct) : command.ExecuteNonQueryAsync(ct);

    private long Begin(DbCommand? command, string origin)
    {
        Hooks!.Executing?.Invoke(this, command, origin);
        return Stopwatch.GetTimestamp();
    }

    private void End(string origin, long started, Exception? error) => Hooks!.Executed?.Invoke(this, origin, Stopwatch.GetElapsedTime(started), error);
}
