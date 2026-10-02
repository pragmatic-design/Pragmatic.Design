using System.Threading.Channels;
using Pragmatic.Email.Configuration;

namespace Pragmatic.Email.Smtp;

/// <summary>
///     Async connection pool for SMTP connections. Uses Channel&lt;T&gt; for async acquire/release.
///     Recycles connections after MaxMessagesPerConnection. Evicts idle connections.
/// </summary>
internal sealed class SmtpConnectionPool(SmtpTransportOptions options) : IAsyncDisposable
{
    private readonly Channel<SmtpConnection> _available = Channel.CreateBounded<SmtpConnection>(new BoundedChannelOptions(options.MaxConnections)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    private readonly SemaphoreSlim _connectionLimiter = new(options.MaxConnections, options.MaxConnections);
    private int _totalCreated;
    // 1 = disposed, 0 = live. Use Interlocked for lock-free thread-safe reads/writes.
    private int _disposed;

    public async Task<SmtpConnection> AcquireAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(SmtpConnectionPool));

        // Try to get an existing connection
        if (_available.Reader.TryRead(out var existing))
        {
            if (IsUsable(existing))
                return existing;

            // Connection is stale or exhausted — dispose and create new.
            // The permit taken when this connection was created MUST be returned here: otherwise
            // every eviction leaks one permit and after MaxConnections evictions AcquireAsync blocks
            // forever. With the default 30s idle timeout that is reached by any app that sends
            // e-mails further apart than the timeout.
            await existing.DisposeAsync().ConfigureAwait(false);
            _connectionLimiter.Release();
        }

        // Create a new connection (respecting pool limit)
        await _connectionLimiter.WaitAsync(ct).ConfigureAwait(false);

        var connection = new SmtpConnection();
        try
        {
            await connection.ConnectAsync(options, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _totalCreated);
            return connection;
        }
        catch
        {
            _connectionLimiter.Release();
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask ReleaseAsync(SmtpConnection connection)
    {
        // Read _disposed with Interlocked to prevent a torn read with concurrent DisposeAsync.
        if (Volatile.Read(ref _disposed) == 1 || !IsUsable(connection))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            _connectionLimiter.Release();
            return;
        }

        if (!_available.Writer.TryWrite(connection))
        {
            // Pool is full — discard
            await connection.DisposeAsync().ConfigureAwait(false);
            _connectionLimiter.Release();
        }
    }

    private bool IsUsable(SmtpConnection connection)
    {
        if (!connection.IsConnected)
            return false;

        if (connection.MessagesSent >= options.MaxMessagesPerConnection)
            return false;

        var idleSeconds = (DateTimeOffset.UtcNow - connection.LastUsedAt).TotalSeconds;
        if (idleSeconds > options.IdleTimeoutSeconds)
            return false;

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        // Atomically mark disposed so that concurrent ReleaseAsync calls see the flag immediately.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return; // Already disposed — prevent double-dispose.

        _available.Writer.TryComplete();

        // Drain already-pooled connections. Connections currently being acquired by AcquireAsync
        // will observe _disposed=1 in their next ReleaseAsync call and dispose themselves then.
        while (_available.Reader.TryRead(out var connection))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _connectionLimiter.Dispose();
    }
}
