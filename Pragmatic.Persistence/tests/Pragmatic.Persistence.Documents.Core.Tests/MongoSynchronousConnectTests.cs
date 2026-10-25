using System.Net;
using System.Net.Sockets;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The synchronous open's connect, without a server: what it does where nothing listens and where the time is up. The connect that succeeds,
///     handshake and SCRAM on the synchronous calls, is <see cref="MongoDriverTests" />'s.
/// </summary>
public class MongoSynchronousConnectTests
{
    [Fact]
    public async Task A_port_nothing_listens_on_is_refused_not_timed_out()
    {
        await using var client = new MongoClient(new MongoSettings { Host = "127.0.0.1", Port = ClosedPort(), ConnectTimeout = TimeSpan.FromSeconds(10) });

        var refused = Assert.Throws<SocketException>(() => client.RunCommand("admin", w => w.Write("ping", 1)));
        Assert.Equal(SocketError.ConnectionRefused, refused.SocketErrorCode);
    }

    [Fact]
    public async Task A_connect_timeout_spent_before_the_first_address_is_a_timeout()
    {
        await using var client = new MongoClient(new MongoSettings { Host = "127.0.0.1", Port = ClosedPort(), ConnectTimeout = TimeSpan.Zero });

        Assert.Throws<TimeoutException>(() => client.RunCommand("admin", w => w.Write("ping", 1)));
    }

    // A port that was free a moment ago: bound, read, released.
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
