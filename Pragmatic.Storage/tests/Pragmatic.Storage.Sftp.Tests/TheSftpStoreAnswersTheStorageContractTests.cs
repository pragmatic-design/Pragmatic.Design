using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Conformance;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     <see cref="SftpFileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. This one refused from <c>GetAsync</c> and was never asked about the other three — the
///     kind of gap a shared contract closes by construction rather than by someone noticing.
///     <para>
///         It builds its own client rather than deriving from <c>SftpStorageTestBase</c>: the contract
///         is the base class here, and one base is all C# allows.
///     </para>
/// </remarks>
public sealed class TheSftpStoreAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    protected override IFileStorage CreateStorage()
    {
        var factory = new SftpClientFactoryMock();
        factory.Create.Returns(new SftpClientMock());

        return new SftpFileStorage(
            new SftpStorageOptions { Host = "host", Username = "user", Password = "pass" },
            factory,
            NullLogger<SftpFileStorage>.Instance);
    }

    protected override Uri ForeignUri { get; } = new("https://evil.example.com/photos/x.png");
}
