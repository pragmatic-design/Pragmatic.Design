namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Covers the URI-host validation of <see cref="AzureBlobFileStorage" />: read/exists/delete
///     with a URI that does not belong to the configured storage account must be rejected with
///     <see cref="ArgumentException" /> before any SDK call, preventing access to arbitrary accounts.
/// </summary>
public sealed class AzureBlobFileStorageSecurityTests
{
    private static readonly Uri ForeignUri = new("https://evil.blob.core.windows.net/photos/blob.bin");

    private readonly AzureBlobFileStorage _storage = new AzureStorageSubstituteFixture().CreateStorage();

    [Fact]
    public async Task GetAsync_HostMismatch_ThrowsArgumentException()
    {
        var act = () => _storage.GetAsync(ForeignUri);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }

    [Fact]
    public async Task ExistsAsync_HostMismatch_ThrowsArgumentException()
    {
        var act = () => _storage.ExistsAsync(ForeignUri);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }

    [Fact]
    public async Task DeleteAsync_HostMismatch_ThrowsArgumentException()
    {
        var act = () => _storage.DeleteAsync(ForeignUri);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }
}
