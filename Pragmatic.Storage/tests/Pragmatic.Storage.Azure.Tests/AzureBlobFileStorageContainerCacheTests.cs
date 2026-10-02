using Azure;
using Azure.Storage.Blobs.Models;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Covers the container-creation cache of <see cref="AzureBlobFileStorage" />:
///     a failed <c>CreateIfNotExistsAsync</c> must NOT be cached as success, so the next
///     save retries the creation instead of failing confusingly on the upload.
/// </summary>
public sealed class AzureBlobFileStorageContainerCacheTests
{
    private readonly AzureStorageSubstituteFixture _fixture = new();

    [Fact]
    public async Task SaveAsync_WhenCreateContainerFails_RetriesCreationOnNextSave()
    {
        var calls = 0;
        // The 3-parameter convenience overload is non-virtual sugar: the interceptable call
        // is the 4-parameter virtual overload with BlobContainerEncryptionScopeOptions.
        _fixture.Container.CreateIfNotExistsAsync4Setup.Returns((_, _, _, _) =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<Response<BlobContainerInfo>>(new RequestFailedException("transient failure"))
                    : Task.FromResult<Response<BlobContainerInfo>>(null!);
            });

        var storage = _fixture.CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        var firstAttempt = () => storage.SaveAsync(stream, "a.bin", "docs");
        await firstAttempt.Should().ThrowAsync<RequestFailedException>();

        stream.Position = 0;
        var uri = await storage.SaveAsync(stream, "a.bin", "docs");

        uri.Should().Be(_fixture.BlobUri);
        calls.Should().Be(2, "the failed creation must not be cached as success");
    }

    [Fact]
    public async Task SaveAsync_AfterSuccessfulCreate_DoesNotRetryCreation()
    {
        var calls = 0;
        _fixture.Container.CreateIfNotExistsAsync4Setup.Returns((_, _, _, _) =>
            {
                calls++;
                return Task.FromResult<Response<BlobContainerInfo>>(null!);
            });

        var storage = _fixture.CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "a.bin", "docs");
        stream.Position = 0;
        await storage.SaveAsync(stream, "b.bin", "docs");

        calls.Should().Be(1, "a successfully created container is cached");
    }
}
