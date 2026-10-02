using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Conformance;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     <see cref="GoogleCloudFileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. This one already behaved; inheriting the contract is what keeps it from drifting
///     apart from the others.
/// </remarks>
public sealed class TheGoogleCloudStoreAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    protected override IFileStorage CreateStorage()
        => new GoogleCloudFileStorage(
            new StorageClientMock(),
            new GoogleCloudStorageOptions { BucketName = "bkt" },
            NullLogger<GoogleCloudFileStorage>.Instance);

    protected override Uri ForeignUri { get; } = new("https://evil.example.com/photos/x.jpg");
}
