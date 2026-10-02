using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class QrCodeAsyncTests
{
    [NativeRequiredFact]
    public async Task GeneratePngAsync_ProducesValidPng()
    {
        var png = await QrCode.GeneratePngAsync("https://www.pragmaticdesign.net");

        png.Should().NotBeEmpty();
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50);
    }

    [NativeRequiredFact]
    public async Task GeneratePngAsync_ToStream_WritesData()
    {
        using var ms = new MemoryStream();

        await QrCode.GeneratePngAsync("test", ms);

        ms.Length.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public async Task GeneratePngAsync_Cancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => QrCode.GeneratePngAsync("test", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
