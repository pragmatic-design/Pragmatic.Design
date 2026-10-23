using System.Data;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql;

/// <summary>The plan's single-row, scalar and command terminals: one row, one value, the rows affected.</summary>
public sealed partial class SqlPlan<T>
{
    /// <summary>The first row or default: the plan's SQL already limits the rows to one.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T?> FirstOrDefaultAsync(SqlQuery<T> query, CancellationToken ct)
    {
        if (Load is not null)
        {
            var rows = await ToListAsync(query, ct).ConfigureAwait(false);
            return rows.Count > 0 ? rows[0] : default;
        }

        var command = Command(query);
        var execution = SqlTelemetry.Begin(query.Session, Origin, command);
        Exception? failure = null;
        try
        {
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read!(reader, query.Session.Dialect) : default;
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            Release(command, execution, query.Session, failure);
        }
    }

    /// <summary>An aggregate: one row, one column, read by the plan's reader; default when SQL answers NULL (no rows).</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> ScalarAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var command = Command(query);
        var execution = SqlTelemetry.Begin(query.Session, Origin, command);
        Exception? failure = null;
        try
        {
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) && !reader.IsDBNull(0) ? Read!(reader, query.Session.Dialect) : default!;
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            Release(command, execution, query.Session, failure);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> FirstAsync(SqlQuery<T> query, CancellationToken ct)
        => await FirstOrDefaultAsync(query, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("The query returned no rows.");

    /// <summary>Exactly one row: the plan's SQL limits the rows to two, so a second one is the error.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> SingleAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var rows = await ToListAsync(query, ct).ConfigureAwait(false);
        return rows.Count switch
        {
            1 => rows[0],
            0 => throw new InvalidOperationException("The query returned no rows."),
            _ => throw new InvalidOperationException("The query returned more than one row."),
        };
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T?> SingleOrDefaultAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var rows = await ToListAsync(query, ct).ConfigureAwait(false);
        return rows.Count switch
        {
            1 => rows[0],
            0 => default,
            _ => throw new InvalidOperationException("The query returned more than one row."),
        };
    }

    /// <summary>An UPDATE or DELETE plan: the rows affected.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ExecuteAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var command = Command(query);
        var execution = SqlTelemetry.Begin(query.Session, Origin, command);
        Exception? failure = null;
        try
        {
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            Release(command, execution, query.Session, failure);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> CountAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var command = Command(query);
        var execution = SqlTelemetry.Begin(query.Session, Origin, command);
        Exception? failure = null;
        try
        {
            return SqlValue.Count(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            Release(command, execution, query.Session, failure);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<bool> AnyAsync(SqlQuery<T> query, CancellationToken ct)
    {
        var command = Command(query);
        var execution = SqlTelemetry.Begin(query.Session, Origin, command);
        Exception? failure = null;
        try
        {
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            Release(command, execution, query.Session, failure);
        }
    }
}
