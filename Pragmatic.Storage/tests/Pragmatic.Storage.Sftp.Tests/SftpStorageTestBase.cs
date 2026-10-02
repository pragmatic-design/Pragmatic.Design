using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Renci.SshNet;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Shared fixture: a substituted <see cref="ISftpClient" /> handed out by a substituted
///     <see cref="ISftpClientFactory" />, and a helper to build a <see cref="SftpFileStorage" />
///     wired to them. No network is touched.
/// </summary>
public abstract class SftpStorageTestBase
{
    protected readonly SftpClientMock Client = new SftpClientMock();
    protected readonly SftpClientFactoryMock Factory = new SftpClientFactoryMock();

    protected SftpStorageTestBase()
        => Factory.Create.Returns(Client);

    protected SftpFileStorage CreateStorage(SftpStorageOptions? options = null)
        => new(
            options ?? new SftpStorageOptions { Host = "host", Username = "user", Password = "pass" },
            Factory,
            NullLogger<SftpFileStorage>.Instance);
}
