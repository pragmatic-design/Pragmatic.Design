using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Wires a mocked <see cref="BlobServiceClient" /> → <see cref="BlobContainerClient" /> →
///     <see cref="BlobClient" /> chain so <see cref="AzureBlobFileStorage" /> can be exercised
///     without any network access. Captures the container name, blob name, upload headers and
///     upload stream observed by the SDK mocks.
/// </summary>
/// <remarks>
///     The Azure SDK ships these clients as classes, so the mocks derive from them and configure
///     through <c>…Setup</c> members — the plain name belongs to the override the storage calls.
/// </remarks>
internal sealed class AzureStorageSubstituteFixture
{
    public const string AccountHost = "account.blob.core.windows.net";

    public BlobServiceClientMock Service { get; } = new();
    public BlobContainerClientMock Container { get; } = new();
    public BlobClientMock Blob { get; } = new();

    /// <summary>The URI the substitute blob client reports for the uploaded blob.</summary>
    public Uri BlobUri { get; } = new($"https://{AccountHost}/photos/blob.bin");

    public string? RequestedContainerName { get; private set; }
    public string? RequestedBlobName { get; private set; }
    public BlobHttpHeaders? UploadHeaders { get; private set; }

    public AzureStorageSubstituteFixture()
    {
        Service.UriSetup.Returns(new Uri($"https://{AccountHost}"));
        Service.GetBlobContainerClientSetup.When(Arg.Do<string>(n => RequestedContainerName = n)).Returns(Container);
        Container.GetBlobClientSetup.When(Arg.Do<string>(n => RequestedBlobName = n)).Returns(Blob);
        Blob.UriSetup.Returns(BlobUri);

        // Eight parameters, so the upload is configured through the argument list. Index 1 is the
        // headers the storage built, which several tests assert on.
        Blob.UploadAsync8Setup.Returns(args =>
        {
            UploadHeaders = (BlobHttpHeaders?)args[1];
            return Task.FromResult<Response<BlobContentInfo>>(null!);
        });
    }

    /// <summary>
    ///     Reconfigures the upload to fully consume the input stream (as the real SDK does), so
    ///     <see cref="LimitedReadStream" /> enforcement mid-upload can be observed.
    /// </summary>
    public void UploadConsumesStream()
        => Blob.UploadAsync8Setup.Returns(args =>
        {
            ((Stream)args[0]!).CopyTo(Stream.Null);
            return Task.FromResult<Response<BlobContentInfo>>(null!);
        });

    public AzureBlobFileStorage CreateStorage(AzureBlobStorageOptions? options = null)
        => new(Service, options ?? new AzureBlobStorageOptions(), NullLogger<AzureBlobFileStorage>.Instance);
}
