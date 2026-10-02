using Pragmatic.Storage.Conformance;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     <see cref="AzureBlobFileStorage" /> against the shared storage contract.
/// </summary>
/// <remarks>
///     The cases are in <see cref="AForeignUriIsACallerErrorContract" />, written once for every
///     provider. This one already behaved; inheriting the contract is what keeps it from drifting
///     apart from the others.
/// </remarks>
public sealed class TheAzureBlobStoreAnswersTheStorageContractTests : AForeignUriIsACallerErrorContract
{
    protected override IFileStorage CreateStorage() => new AzureStorageSubstituteFixture().CreateStorage();

    /// <remarks>Another account's host, which is the shape this provider's guard is written against.</remarks>
    protected override Uri ForeignUri { get; } = new("https://evil.blob.core.windows.net/photos/blob.bin");
}
