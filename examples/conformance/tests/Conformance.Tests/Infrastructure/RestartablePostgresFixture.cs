using System.Net;
using System.Net.Sockets;
using Testcontainers.PostgreSql;

namespace Conformance.Tests.Infrastructure;

/// <summary>
///     A PostgreSQL container that can be restarted under a running host.
/// </summary>
/// <remarks>
///     <para>
///         The host port is <b>fixed</b>: a container restarted on a new port would be another address, and
///         the running host does not re-read its connection string. The restart closes every pooled
///         connection from the server side, which is exactly what happens in production when the database
///         restarts.
///     </para>
///     <para>
///         ⚠️ A container of its own, not the shared one: a restart in the middle of the other cases would
///         make their outcome order-dependent.
///     </para>
/// </remarks>
public sealed class RestartablePostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("conformance")
        .WithUsername("pragmatic")
        .WithPassword("Pragmatic@Test!")
        .WithPortBinding(FreeTcpPort(), PostgreSqlBuilder.PostgreSqlPort)
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        ConnectionString = _container.GetConnectionString();
        await ConformanceSchema.ApplyAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <summary>Stops and restarts the database, with its data and on the same port.</summary>
    public async Task RestartAsync()
    {
        await _container.StopAsync().ConfigureAwait(false);
        await _container.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
