using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     The Result-shaped way to ask storage a question.
/// </summary>
/// <remarks>
///     <para>
///         The framework's own rule is Result over exceptions, and these are the two places where
///         storage offers it: a missing file becomes an error value instead of a null, and a provider
///         failure becomes one instead of an exception. Nothing in the repository called them, no test
///         ran them and no page named them — the prescribed form was the invisible one.
///     </para>
///     <para>
///         What each asserts is the mapping, because that is the whole reason to prefer them: which
///         error a caller gets decides whether the endpoint answers 404 or 500.
///     </para>
/// </remarks>
public class ResultExtensionsTests
{
    private sealed class Provider(StoredFileInfo? info = null, Exception? throws = null)
        : IFileInfoProvider, ISignedUrlProvider
    {
        public Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
            => throws is not null ? Task.FromException<StoredFileInfo?>(throws) : Task.FromResult(info);

        public Task<Uri> GetDownloadUrlAsync(Uri fileUri, TimeSpan expiry, CancellationToken ct = default)
            => throws is not null
                ? Task.FromException<Uri>(throws)
                : Task.FromResult(new Uri("https://files.test/signed"));
    }

    private static readonly Uri AFile = new("https://files.test/a.pdf");

    [Fact]
    public async Task GetInfoAsResult_WhenTheFileIsThere_CarriesIt()
    {
        var provider = new Provider(new StoredFileInfo { SizeBytes = 12, FileUri = AFile });

        var result = await provider.GetInfoAsResultAsync(AFile);

        result.IsSuccess.Should().BeTrue();
        result.Value.SizeBytes.Should().Be(12);
    }

    /// <remarks>
    ///     The null the underlying provider returns is exactly what this exists to translate: a caller
    ///     that forgets to check it reads a missing file as a present one.
    /// </remarks>
    [Fact]
    public async Task GetInfoAsResult_WhenTheFileIsMissing_IsNotFoundRatherThanNull()
    {
        var provider = new Provider();

        var result = await provider.GetInfoAsResultAsync(AFile);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<StorageFileNotFoundError>();
    }

    /// <remarks>
    ///     A provider that fails is a different answer from a file that is not there, and the
    ///     distinction is what lets an endpoint tell 404 from 500.
    /// </remarks>
    [Fact]
    public async Task GetInfoAsResult_WhenTheProviderFails_IsAWriteErrorNotANotFound()
    {
        var provider = new Provider(throws: new InvalidOperationException("the bucket is unreachable"));

        var result = await provider.GetInfoAsResultAsync(AFile);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<StorageWriteError>();
    }

    /// <remarks>
    ///     Cancellation is not a storage failure, so it travels as itself rather than being folded
    ///     into an error value the caller would treat as a bad bucket.
    /// </remarks>
    [Fact]
    public async Task GetInfoAsResult_WhenCancelled_Throws()
    {
        var provider = new Provider(throws: new OperationCanceledException());

        var act = async () => await provider.GetInfoAsResultAsync(AFile).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetDownloadUrlAsResult_WhenTheProviderAnswers_CarriesTheUrl()
    {
        var provider = new Provider();

        var result = await provider.GetDownloadUrlAsResultAsync(AFile, TimeSpan.FromMinutes(15));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new Uri("https://files.test/signed"));
    }

    [Fact]
    public async Task GetDownloadUrlAsResult_WhenTheProviderFails_IsAnErrorNotAnException()
    {
        var provider = new Provider(throws: new InvalidOperationException("no credentials"));

        var result = await provider.GetDownloadUrlAsResultAsync(AFile, TimeSpan.FromMinutes(15));

        result.IsSuccess.Should().BeFalse();
    }
}
