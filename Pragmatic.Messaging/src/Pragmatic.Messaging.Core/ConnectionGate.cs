namespace Pragmatic.Messaging;

/// <summary>
///     What an operation waits on while its transport's connect is still in progress.
/// </summary>
/// <remarks>
///     <para>
///         A transport whose connect does I/O — RabbitMQ opens a connection, SQL creates its schema — is
///         connected in the background by its consumer service, so an application starts with its broker
///         down. Until that connect completes, a publish issued as soon as the host has started (a startup
///         step, the first request, a test) finds the transport not connected. Rather than fail, it
///         waits here: for the connect to succeed, to fail, or for the timeout to pass.
///     </para>
///     <para>
///         The transport calls <see cref="Begin" /> before it reports <see cref="TransportStatus.Connecting" />,
///         so an operation that reads that status always finds the current attempt to wait on.
///     </para>
/// </remarks>
public sealed class ConnectionGate
{
    private volatile TaskCompletionSource _attempt = NewAttempt();

    /// <summary>A connect starts: operations issued from now on wait for this one.</summary>
    public void Begin() => _attempt = NewAttempt();

    /// <summary>The connect succeeded: the operations waiting on it proceed.</summary>
    public void Opened() => _attempt.TrySetResult();

    /// <summary>The connect failed: the operations waiting on it fail with its reason.</summary>
    public void Failed(Exception reason) => _attempt.TrySetException(reason);

    /// <summary>
    ///     Waits for the connect in progress, up to <paramref name="timeout" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The connect failed, or did not complete within <paramref name="timeout" />.
    /// </exception>
    public async Task WaitAsync(string transportName, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await _attempt.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"{transportName} transport did not connect within {timeout}: the broker is not reachable yet.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"{transportName} transport failed to connect.", ex);
        }
    }

    private static TaskCompletionSource NewAttempt() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
