using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Configuration;
using Pragmatic.Email.Smtp;

namespace Pragmatic.Email.Tests.Unit.Smtp;

public sealed class SmtpConnectionPoolTests
{
    private static SmtpTransportOptions Options(int maxConnections, int idleTimeoutSeconds) => new()
    {
        Host = "127.0.0.1",
        UseSsl = false,
        MaxConnections = maxConnections,
        IdleTimeoutSeconds = idleTimeoutSeconds,
        MaxMessagesPerConnection = 100,
    };

    [Fact]
    public async Task AcquireAsync_AfterRepeatedStaleEvictions_StillServesConnections()
    {
        // An evicted connection must return its permit to the limiter. Disposed without it, after
        // MaxConnections evictions the semaphore is exhausted and AcquireAsync blocks forever — with
        // the 30s default idle timeout, any app sending e-mails further apart than the timeout would
        // stop sending entirely until the process restarted.
        using var server = new FakeSmtpServer();
        var options = Options(maxConnections: 2, idleTimeoutSeconds: 1);
        options.Port = server.Port;

        var pool = new SmtpConnectionPool(options);

        // Each cycle parks a fresh connection in the pool, lets it go stale, then evicts it on acquire.
        for (var i = 0; i < options.MaxConnections; i++)
        {
            var connection = await pool.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
            await pool.ReleaseAsync(connection).ConfigureAwait(true);
            await Task.Delay(1200).ConfigureAwait(true);
        }

        var acquire = pool.AcquireAsync(CancellationToken.None);
        var finished = await Task.WhenAny(acquire, Task.Delay(5000)).ConfigureAwait(true);

        finished.Should().BeSameAs(acquire, "the pool must not deadlock after stale evictions");

        var acquired = await acquire.ConfigureAwait(true);
        await pool.ReleaseAsync(acquired).ConfigureAwait(true);
        await pool.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AcquireAsync_ReusesAPooledConnection()
    {
        using var server = new FakeSmtpServer();
        var options = Options(maxConnections: 2, idleTimeoutSeconds: 60);
        options.Port = server.Port;

        var pool = new SmtpConnectionPool(options);

        var first = await pool.AcquireAsync(CancellationToken.None).ConfigureAwait(true);
        await pool.ReleaseAsync(first).ConfigureAwait(true);
        var second = await pool.AcquireAsync(CancellationToken.None).ConfigureAwait(true);

        second.Should().BeSameAs(first, "a still-usable connection must be reused, not recreated");

        await pool.ReleaseAsync(second).ConfigureAwait(true);
        await pool.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AcquireAsync_AfterDispose_Throws()
    {
        using var server = new FakeSmtpServer();
        var options = Options(maxConnections: 1, idleTimeoutSeconds: 60);
        options.Port = server.Port;

        var pool = new SmtpConnectionPool(options);
        await pool.DisposeAsync().ConfigureAwait(true);

        var act = async () => await pool.AcquireAsync(CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
    }
}
