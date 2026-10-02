using FluentFTP;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Builds an <see cref="FtpFileStorage" /> over a substituted <see cref="IAsyncFtpClient" /> so
///     the provider can be exercised without a live server.
/// </summary>
internal sealed class FtpStorageTestContext
{
    // Typed as the mocks, not the interfaces: the configurable members live on the mock, and a test
    // holding an IAsyncFtpClient can only call the client, never set it up.
    public AsyncFtpClientMock Client { get; } = new();
    public AsyncFtpClientFactoryMock Factory { get; }
    public FtpFileStorage Storage { get; }

    public FtpStorageTestContext(FtpStorageOptions options)
    {
        Factory = new AsyncFtpClientFactoryMock();
        Factory.Create.Returns(Client);
        Storage = new FtpFileStorage(options, Factory, NullLogger<FtpFileStorage>.Instance);
    }

    public static FtpStorageTestContext Create(FtpStorageOptions? options = null)
        => new(options ?? new FtpStorageOptions { Host = "ftp.example.com", Username = "user" });
}
