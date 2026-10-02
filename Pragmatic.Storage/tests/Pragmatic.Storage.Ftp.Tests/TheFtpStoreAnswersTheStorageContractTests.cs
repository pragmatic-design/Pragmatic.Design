using Pragmatic.Storage.Conformance;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     <see cref="FtpFileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. This one refused from <c>GetAsync</c> and was never asked about the other three — the
///     kind of gap a shared contract closes by construction rather than by someone noticing.
/// </remarks>
public sealed class TheFtpStoreAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    protected override IFileStorage CreateStorage() => FtpStorageTestContext.Create().Storage;

    protected override Uri ForeignUri { get; } = new("https://evil.example.com/photos/x.jpg");
}
