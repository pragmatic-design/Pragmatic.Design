using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.ClaimCheck;
using Pragmatic.Storage.Local;

namespace Pragmatic.Messaging.Tests.Unit;

public sealed class FileStorageClaimCheckStoreTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"claimcheck-{Guid.NewGuid():N}");
    private readonly FileStorageClaimCheckStore _store;

    public FileStorageClaimCheckStoreTests()
        => _store = new FileStorageClaimCheckStore(
            new LocalDiskFileStorage(_basePath, NullLogger<LocalDiskFileStorage>.Instance),
            NullLogger<FileStorageClaimCheckStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
            Directory.Delete(_basePath, recursive: true);
    }

    [Fact]
    public async Task StoreRetrieve_Roundtrip_ReturnsOriginalPayload()
    {
        var payload = Encoding.UTF8.GetBytes(new string('p', 100_000));

        var reference = await _store.StoreAsync(new MemoryStream(payload, writable: false));
        using var retrieved = await _store.RetrieveAsync(reference);
        using var buffer = new MemoryStream();
        await retrieved.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(payload);
        reference.Should().Contain(FileStorageClaimCheckStore.Container);
    }

    [Fact]
    public async Task Retrieve_AfterDelete_ThrowsWithGuidance()
    {
        var reference = await _store.StoreAsync(new MemoryStream([1, 2, 3], writable: false));
        await _store.DeleteAsync(reference);

        var act = () => _store.RetrieveAsync(reference);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*DeleteAfterConsume*");
    }

    [Fact]
    public async Task Delete_MissingReference_DoesNotThrow()
    {
        var act = () => _store.DeleteAsync("/files/claim-checks/nonexistent.bin");
        await act.Should().NotThrowAsync();
    }

    // The reference arrives in the x-claim-check header and is therefore attacker-controlled.
    // Only the exact shape StoreAsync writes may reach the storage provider.
    [Theory]
    [InlineData("/files/claim-checks/../../../etc/passwd")]          // path traversal
    [InlineData("/files/other-tenant-blobs/0123456789abcdef0123456789abcdef.bin")] // different container
    [InlineData("/files/claim-checks/evil.bin")]                      // name is not a generated id
    [InlineData("https://attacker.example/claim-checks/not-a-guid.bin")] // foreign absolute URI
    public async Task Retrieve_HostileReference_IsRejected(string reference)
    {
        var act = () => _store.RetrieveAsync(reference);

        (await act.Should().ThrowAsync<InvalidOperationException>(
                "a reference that does not match '<container>/<id>.bin' must never reach the storage provider"))
            .WithMessage("*Rejected claim-check reference*");
    }
}
