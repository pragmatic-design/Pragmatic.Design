using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging;

/// <summary>
///     Keeps connecting a transport whose consumer service connects it in the background, until it
///     connects, the retry gives up, or the host stops.
/// </summary>
/// <remarks>
///     <para>
///         An application starts with its broker down: that is why the consumer service does not await the
///         connect before the host reports started. A connect that failed used to fault the service's
///         <c>ExecuteAsync</c>, and the host's default <c>BackgroundServiceExceptionBehavior.StopHost</c>
///         then stopped the whole application, the opposite of what starting without the broker is for.
///     </para>
///     <para>
///         Between attempts the transport is not connected, so an operation fails at once instead of
///         waiting. Giving up leaves the host running with that same behaviour, logged as an error, rather
///         than stopping it.
///     </para>
/// </remarks>
public static partial class TransportConnectLoop
{
    /// <summary>
    ///     Awaits <paramref name="firstAttempt" />, then reconnects <paramref name="transport" /> after each
    ///     failure as <paramref name="retry" /> says.
    /// </summary>
    /// <returns>True once connected; false when <see cref="ConnectRetry.MaxAttempts" /> ran out.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct" /> was cancelled: the host is stopping.</exception>
    public static async Task<bool> UntilConnectedAsync(
        IMessageTransport transport, Task firstAttempt, ConnectRetry retry, ILogger logger, CancellationToken ct)
    {
        Ensure.Ensure.ThrowIfNull(transport);
        _ = Ensure.Ensure.ThrowIfNull(firstAttempt);
        Ensure.Ensure.ThrowIfNull(retry);
        Ensure.Ensure.ThrowIfNull(logger);

        var attempt = 1;
        var connecting = firstAttempt;
        while (true)
        {
            try
            {
                await connecting.ConfigureAwait(false);
                if (attempt > 1)
                    LogConnectedAfterRetries(logger, transport.Name, attempt);
                return true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                if (retry.MaxAttempts > 0 && attempt >= retry.MaxAttempts)
                {
                    LogGaveUp(logger, transport.Name, attempt, ex);
                    return false;
                }

                var delay = retry.DelayAfter(attempt);
                LogRetrying(logger, transport.Name, attempt, delay, ex);
                await Task.Delay(delay, ct).ConfigureAwait(false);

                attempt++;
                connecting = transport.ConnectAsync(ct);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Transport} transport failed to connect (attempt {Attempt}); retrying in {Delay}")]
    private static partial void LogRetrying(ILogger logger, string transport, int attempt, TimeSpan delay, Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Transport} transport connected at attempt {Attempt}")]
    private static partial void LogConnectedAfterRetries(ILogger logger, string transport, int attempt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "{Transport} transport did not connect in {Attempts} attempts; giving up. The host keeps running, and every operation on this transport fails until it is restarted")]
    private static partial void LogGaveUp(ILogger logger, string transport, int attempts, Exception ex);
}
