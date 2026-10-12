namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers what <see cref="PipedDownloadStream.StartAsync" /> returns before the caller reads:
///     <see langword="null" /> for a missing file, the failure of a download that never wrote, and an
///     empty stream for an empty file. The streaming itself is measured in each provider's suite.
/// </summary>
public sealed class PipedDownloadStreamTests
{
    [Fact]
    public async Task StartAsync_DownloadReportsMissing_ReturnsNull()
    {
        var stream = await PipedDownloadStream.StartAsync((_, _) => Task.FromResult(false), CancellationToken.None);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_DownloadFailsBeforeWriting_Throws()
    {
        var act = () => PipedDownloadStream.StartAsync(
            (_, _) => Task.FromException<bool>(new IOException("refused")), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>().WithMessage("refused");
    }

    [Fact]
    public async Task StartAsync_EmptyFile_ReturnsAnEmptyStream()
    {
        using var stream = await PipedDownloadStream.StartAsync((_, _) => Task.FromResult(true), CancellationToken.None);

        stream.Should().NotBeNull();
        using var read = new MemoryStream();
        await stream!.CopyToAsync(read);
        read.Length.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_SmallFile_RoundTripsTheBytes()
    {
        byte[] payload = [1, 2, 3, 4, 5];

        using var stream = await PipedDownloadStream.StartAsync(async (destination, ct) =>
        {
            await destination.WriteAsync(payload, ct).ConfigureAwait(false);
            return true;
        }, CancellationToken.None);

        using var read = new MemoryStream();
        await stream!.CopyToAsync(read);
        read.ToArray().Should().Equal(payload);
    }
}
