using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

// Managed-only: every case here throws during argument validation,
// BEFORE the native pragmatic_qr_generate P/Invoke is reached.
public class QrCodeArgumentValidationTests
{
    [Fact]
    public void GeneratePng_NullText_ThrowsArgumentNull()
    {
        var act = () => QrCode.GeneratePng(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("text");
    }

    [Fact]
    public void GeneratePng_EmptyText_ThrowsArgumentExceptionForText()
    {
        var act = () => QrCode.GeneratePng("");

        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [Fact]
    public void GeneratePng_ZeroModuleSize_ThrowsArgumentExceptionForModuleSize()
    {
        var act = () => QrCode.GeneratePng("data", moduleSize: 0);

        act.Should().Throw<ArgumentException>().WithParameterName("moduleSize");
    }

    [Fact]
    public void GeneratePng_ModuleSizeAboveMax_ThrowsArgumentExceptionForModuleSize()
    {
        var act = () => QrCode.GeneratePng("data", moduleSize: 1001);

        act.Should().Throw<ArgumentException>().WithParameterName("moduleSize");
    }

    [Fact]
    public void GeneratePng_MarginAboveMax_ThrowsArgumentExceptionForMargin()
    {
        var act = () => QrCode.GeneratePng("data", margin: 101);

        act.Should().Throw<ArgumentException>().WithParameterName("margin");
    }

    [Fact]
    public void GeneratePng_ToNullStream_ThrowsArgumentNullForOutput()
    {
        var act = () => QrCode.GeneratePng("data", output: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("output");
    }

    [Fact]
    public async Task GeneratePngAsync_ToNullStream_ThrowsArgumentNullForOutput()
    {
        var act = () => QrCode.GeneratePngAsync("data", output: null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("output");
    }
}
