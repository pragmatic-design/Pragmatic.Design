using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Pragmatic.Persistence.Documents.Mongo;
using Xunit;

namespace Pragmatic.Persistence.Documents.Tests;

/// <summary>
///     MongoDB with <c>--tlsMode requireTLS</c> and a self-signed certificate made here: the driver connects in TLS (the certificate accepted as
///     a test's, <c>tlsAllowInvalidCertificates</c>), authenticates and runs a command; a client without TLS is refused.
/// </summary>
public sealed class MongoTlsServer : IAsyncLifetime
{
    private IContainer? _container;

    public string? Unavailable { get; private set; }

    public string Address { get; private set; } = "";

    public async Task InitializeAsync()
    {
        try
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            var pem = certificate.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem() + "\n";
            _container = MongoServer.Start(new ContainerBuilder().WithImage("mongo:8")
                .WithResourceMapping(Encoding.ASCII.GetBytes(pem), "/etc/mongo/server.pem")
                .WithResourceMapping(Encoding.ASCII.GetBytes(certificate.ExportCertificatePem() + "\n"), "/etc/mongo/ca.pem")
                // MongoDB 8 wants a chain of trust with TLS (SERVER-72839): the self-signed certificate is its own CA; clients come without one.
                .WithCommand("--tlsMode", "requireTLS", "--tlsCertificateKeyFile", "/etc/mongo/server.pem", "--tlsCAFile", "/etc/mongo/ca.pem", "--tlsAllowConnectionsWithoutCertificates"));
            await _container.StartAsync();
            Address = $"{_container.Hostname}:{_container.GetMappedPublicPort(27017)}";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var log = await MongoServer.TailAsync(_container);
            Unavailable = "MongoDB with TLS did not start: " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + " — log: " + log;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

public class MongoTlsTests(MongoTlsServer server) : IClassFixture<MongoTlsServer>
{
    // Any start that failed: the gate refuses to run this suite without Docker, so no failure here is Docker's absence.
    [Fact]
    public void The_server_started()
        => Assert.Null(server.Unavailable);

    [Fact]
    public async Task The_driver_speaks_tls_and_a_plain_client_is_refused()
    {
        if (server.Unavailable is not null)
            return;
        await using (var tls = new MongoClient($"mongodb://{MongoServer.User}:{MongoServer.Password}@{server.Address}/?tls=true&tlsAllowInvalidCertificates=true"))
        {
            var reply = await tls.RunCommandAsync("admin", w => w.Write("ping", 1));
            Assert.True(reply.TryGetNumber("ok", out var ok) && ok == 1);
        }

        await using var plain = new MongoClient($"mongodb://{MongoServer.User}:{MongoServer.Password}@{server.Address}/?connectTimeoutMS=5000");
        await Assert.ThrowsAnyAsync<IOException>(() => plain.RunCommandAsync("admin", w => w.Write("ping", 1))); // the server closes a plaintext connection
    }

    [Fact]
    public async Task An_unvalidated_self_signed_certificate_is_refused()
    {
        if (server.Unavailable is not null)
            return;
        await using var strict = new MongoClient($"mongodb://{MongoServer.User}:{MongoServer.Password}@{server.Address}/?tls=true");
        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => strict.RunCommandAsync("admin", w => w.Write("ping", 1)));
    }
}
