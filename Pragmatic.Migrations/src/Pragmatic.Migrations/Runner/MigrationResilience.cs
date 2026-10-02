using System.Data.Common;
using System.Net.Sockets;
using Pragmatic.Resilience;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Resilience pipeline for transient database connection failures during migrations.
///     Retries connection open with exponential backoff + jitter.
/// </summary>
internal static class MigrationResilience
{
    private static readonly IResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(r =>
        {
            r.MaxRetries = 3;
            r.BaseDelay = TimeSpan.FromSeconds(1);
            r.BackoffType = BackoffType.Exponential;
            r.UseJitter = true;
            r.MaxDelay = TimeSpan.FromSeconds(15);
            r.ShouldRetry = IsTransient;
        })
        .AddTimeout(t => t.Timeout = TimeSpan.FromMinutes(2))
        .Build();

    /// <summary>
    ///     Opens a database connection with retry on transient failures.
    /// </summary>
    internal static async Task<DbConnection> OpenWithRetryAsync(
        Func<Task<DbConnection>> factory, CancellationToken ct)
    {
        return await Pipeline.ExecuteAsync(
            async (_, token) => await factory().ConfigureAwait(false),
            new ResilienceContext { OperationName = "Migrations.OpenConnection" },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Decides whether an exception is a transient connection failure worth retrying.
    ///     <para>
    ///     This package is provider-agnostic (it does not reference Npgsql / Microsoft.Data.SqlClient),
    ///     so detection is layered, most-reliable first:
    ///     </para>
    ///     <list type="number">
    ///       <item>Exception <b>type</b> — timeouts, socket failures, and transient
    ///         <see cref="DbException"/> (its <see cref="DbException.IsTransient"/> flag, set by
    ///         ADO.NET providers).</item>
    ///       <item>Provider <b>error codes</b> — <see cref="DbException.SqlState"/> (ANSI SQLSTATE)
    ///         and known SQL Server transient error numbers, which are locale-independent.</item>
    ///       <item>Localized <b>message</b> heuristic — last resort only, since message text varies
    ///         across providers and cultures; documented here as a deliberate fallback.</item>
    ///     </list>
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        // 1) Type-based: locale-independent and the most reliable signal.
        if (ex is TimeoutException or SocketException)
            return true;

        // Unwrap to the innermost provider exception (factory wraps may nest the real cause).
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is SocketException or TimeoutException)
                return true;

            if (cur is DbException db)
            {
                // ADO.NET providers (incl. Npgsql, SqlClient) set IsTransient for retryable faults.
                if (db.IsTransient)
                    return true;

                // 2) Code-based: locale-independent error identifiers.
                //    SqlState (ANSI SQLSTATE) is the only provider-agnostic code on DbException;
                //    provider-specific numbers (e.g. SqlException.Number) require a hard reference
                //    to the provider package, which this package intentionally avoids.
                if (IsTransientSqlState(db.SqlState))
                    return true;
            }
        }

        // 3) Documented fallback: message heuristic. Fragile across providers/locales, kept only
        //    because the type/code signals above are not guaranteed for every ADO.NET provider.
        var msg = ex.Message;
        return msg.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
               (msg.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("broken", StringComparison.OrdinalIgnoreCase));
    }

    // ANSI SQLSTATE classes/codes that indicate a transient connection-level failure.
    // 08xxx = connection exception (PostgreSQL & ANSI); 57P03 = cannot_connect_now (PostgreSQL).
    private static bool IsTransientSqlState(string? sqlState)
    {
        if (string.IsNullOrEmpty(sqlState))
            return false;
        return sqlState.StartsWith("08", StringComparison.Ordinal) // connection_exception family
            || sqlState == "57P03";                                 // cannot_connect_now
    }
}
