using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Conformance;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     <see cref="S3FileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. This one already behaved; inheriting the contract is what keeps it from drifting
///     apart from the others.
/// </remarks>
public sealed class TheS3StoreAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    protected override IFileStorage CreateStorage()
        => new S3FileStorage(
            new AmazonS3Mock(),
            new S3StorageOptions { BucketName = "bkt" },
            NullLogger<S3FileStorage>.Instance);

    protected override Uri ForeignUri { get; } = new("https://evil.example.com/photos/x.jpg");
}
