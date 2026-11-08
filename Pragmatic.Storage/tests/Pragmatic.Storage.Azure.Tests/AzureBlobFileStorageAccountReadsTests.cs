using Azure;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Reading, checking and deleting a blob go through the account's client, as <see cref="AzureBlobFileStorage.SaveAsync" /> and
///     <see cref="AzureBlobFileStorage.GetInfoAsync" /> do. A <c>new BlobClient(uri)</c> carries no credential, so a private blob — every
///     blob <c>SaveAsync</c> writes — answered 403. Measured here through the substitute chain: an operation that reaches the account's
///     container and blob clients is one that carries the account's credential; one that builds its own client never reaches them.
/// </summary>
public sealed class AzureBlobFileStorageAccountReadsTests
{
    private readonly AzureStorageSubstituteFixture _fixture = new();
    private readonly AzureBlobFileStorage _storage;

    public AzureBlobFileStorageAccountReadsTests()
        => _storage = _fixture.CreateStorage();

    [Fact]
    public async Task ExistsAsync_GoesThroughTheAccountsClient()
    {
        _fixture.Blob.ExistsAsyncSetup.Returns(Task.FromResult(Response.FromValue(true, null!)));

        var exists = await _storage.ExistsAsync(_fixture.BlobUri);

        exists.Should().BeTrue();
        (_fixture.RequestedContainerName, _fixture.RequestedBlobName).Should().Be(("photos", "blob.bin"));
    }

    [Fact]
    public async Task DeleteAsync_GoesThroughTheAccountsClient()
    {
        _fixture.Blob.DeleteIfExistsAsyncSetup.Returns(Task.FromResult(Response.FromValue(true, null!)));

        await _storage.DeleteAsync(_fixture.BlobUri);

        (_fixture.RequestedContainerName, _fixture.RequestedBlobName).Should().Be(("photos", "blob.bin"));
    }

    [Fact]
    public async Task GetAsync_OfAMissingBlob_AsksTheAccountsClient()
    {
        _fixture.Blob.ExistsAsyncSetup.Returns(Task.FromResult(Response.FromValue(false, null!)));

        var content = await _storage.GetAsync(_fixture.BlobUri);

        content.Should().BeNull();
        (_fixture.RequestedContainerName, _fixture.RequestedBlobName).Should().Be(("photos", "blob.bin"));
    }
}
