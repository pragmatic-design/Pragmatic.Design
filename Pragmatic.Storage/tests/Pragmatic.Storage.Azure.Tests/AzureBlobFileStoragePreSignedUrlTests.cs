using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Covers the <see cref="ISignedUrlProvider" /> capability of <see cref="AzureBlobFileStorage" />:
///     when the underlying client cannot sign (no shared key credential, so
///     <c>CanGenerateSasUri</c> is false) <see cref="AzureBlobFileStorage.GetDownloadUrlAsync" /> must
///     throw <see cref="NotSupportedException" />; a foreign URI is rejected before that with
///     <see cref="ArgumentException" />.
/// </summary>
public sealed class AzureBlobFileStoragePreSignedUrlTests
{
    private readonly AzureStorageSubstituteFixture _fixture = new();
    private readonly AzureBlobFileStorage _storage;

    public AzureBlobFileStoragePreSignedUrlTests()
        => _storage = _fixture.CreateStorage();

    [Fact]
    public async Task GetDownloadUrlAsync_ClientCannotSign_ThrowsNotSupported()
    {
        _fixture.Blob.CanGenerateSasUriSetup.Returns(false);

        var act = () => _storage.GetDownloadUrlAsync(_fixture.BlobUri, TimeSpan.FromMinutes(15));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("shared key credential");
    }

    [Fact]
    public async Task GetDownloadUrlAsync_ForeignUri_ThrowsArgumentException()
    {
        var act = () => _storage.GetDownloadUrlAsync(
            new Uri("https://evil.blob.core.windows.net/photos/blob.bin"), TimeSpan.FromMinutes(5));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }
}
