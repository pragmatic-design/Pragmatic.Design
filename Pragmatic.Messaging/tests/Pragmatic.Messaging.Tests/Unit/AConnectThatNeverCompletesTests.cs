using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Messaging.Sql;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

/// <summary>
///     The control on "a publish waits for a connect in progress": the wait is bounded. A
///     connect that never completes fails the publish once the transport's wait timeout passes, instead of
///     holding it forever.
/// </summary>
/// <remarks>
///     The server is a socket that accepts and never answers, so the client's handshake hangs with no
///     broker and no network involved: the connect stays in progress for as long as the test needs.
/// </remarks>
public sealed class AConnectThatNeverCompletesTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(300);

    private readonly TcpListener _silent = new(IPAddress.Loopback, 0);

    public AConnectThatNeverCompletesTests() => _silent.Start();

    private int Port => ((IPEndPoint)_silent.LocalEndpoint).Port;

    public void Dispose() => _silent.Stop();

    [Fact]
    public async Task RabbitMq_APublishWaitingOnIt_FailsAfterTheWaitTimeout()
    {
        var transport = new RabbitMqTransport(
            new RabbitMqOptions
            {
                ConnectionString = $"amqp://guest:guest@127.0.0.1:{Port}",
                AutoReconnect = false,
                ConnectWaitTimeout = Wait
            },
            NullLogger<RabbitMqTransport>.Instance);
        try
        {
            _ = transport.ConnectAsync();
            transport.Status.Should().Be(TransportStatus.Connecting);

            var publish = () => transport.PublishAsync("x"u8.ToArray(), "t", MessageContext.New());

            var waited = System.Diagnostics.Stopwatch.StartNew();
            (await publish.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true))
                .WithMessage("*did not connect within*");
            waited.Elapsed.Should().BeGreaterThanOrEqualTo(Wait - TimeSpan.FromMilliseconds(50), "it waited for the connect before giving up");
            transport.Status.Should().Be(TransportStatus.Connecting, "the connect is still hanging: the publish gave up, not the connect");
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task Sql_APublishWaitingOnIt_FailsAfterTheWaitTimeout()
    {
        var options = new SqlTransportOptions
        {
            ConfigureDbContext = db => db.UseNpgsql(
                $"Host=127.0.0.1;Port={Port};Username=u;Password=p;Database=d;Timeout=60"),
            UseNotifications = false,
            ConnectWaitTimeout = Wait
        };
        var builder = new DbContextOptionsBuilder<SqlTransportDbContext>();
        options.ConfigureDbContext(builder);
        var factory = new Factory(builder.Options);
        var transport = new SqlTransport(
            new SqlTransportStorage(factory, options, NullLogger<SqlTransportStorage>.Instance),
            new SqlTransportSchema(factory, NullLogger<SqlTransportSchema>.Instance),
            factory,
            options,
            NullLoggerFactory.Instance);
        try
        {
            _ = transport.ConnectAsync();
            transport.Status.Should().Be(TransportStatus.Connecting);

            var publish = () => transport.PublishAsync("x"u8.ToArray(), "t", MessageContext.New());

            var waited = System.Diagnostics.Stopwatch.StartNew();
            (await publish.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true))
                .WithMessage("*did not connect within*");
            waited.Elapsed.Should().BeGreaterThanOrEqualTo(Wait - TimeSpan.FromMilliseconds(50), "it waited for the connect before giving up");
            transport.Status.Should().Be(TransportStatus.Connecting, "the connect is still hanging: the publish gave up, not the connect");
        }
        finally
        {
            await transport.DisposeAsync().ConfigureAwait(true);
        }
    }

    private sealed class Factory(DbContextOptions<SqlTransportDbContext> options)
        : IDbContextFactory<SqlTransportDbContext>
    {
        public SqlTransportDbContext CreateDbContext() => new(options);
    }
}
