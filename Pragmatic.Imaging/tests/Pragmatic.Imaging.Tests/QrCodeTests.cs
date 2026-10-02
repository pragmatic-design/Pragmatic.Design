using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

public class QrCodeTests
{
    [NativeRequiredFact]
    public void GeneratePng_SimpleText_ProducesValidPng()
    {
        var png = QrCode.GeneratePng("https://www.pragmaticdesign.net");

        png.Should().NotBeEmpty();
        // PNG magic
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50);
    }

    [NativeRequiredFact]
    public void GeneratePng_CustomSize_ProducesImage()
    {
        var png = QrCode.GeneratePng("Hello", moduleSize: 5, margin: 1);

        png.Should().NotBeEmpty();

        var info = ImageInfo.FromBytes(png);
        info.Width.Should().BeGreaterThan(0);
        info.Height.Should().BeGreaterThan(0);
        info.Format.Should().Be(ImageFormat.Png);
    }

    [NativeRequiredFact]
    public void GeneratePng_ToStream_Works()
    {
        using var ms = new MemoryStream();
        QrCode.GeneratePng("test", ms);

        ms.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GeneratePng_EmptyText_ThrowsArgumentException()
    {
        var act = () => QrCode.GeneratePng("");

        act.Should().Throw<ArgumentException>();
    }

    [NativeRequiredFact]
    public void GeneratePng_LongText_ProducesLargerImage()
    {
        var shortPng = QrCode.GeneratePng("A");
        var longPng = QrCode.GeneratePng(new string('A', 200));

        var shortInfo = ImageInfo.FromBytes(shortPng);
        var longInfo = ImageInfo.FromBytes(longPng);

        // More data = more modules = larger image
        longInfo.Width.Should().BeGreaterThan(shortInfo.Width);
    }

    [NativeRequiredFact]
    public void GeneratePng_ModuleSizeThatWouldExceedOutputCap_ThrowsImagingException()
    {
        // Managed validation permits moduleSize up to 1000; the native side then bounds the
        // final image dimension to guard against an OOM allocation (a QR bomb).
        // "A" → 21 modules; 21 × 500 = 10 500 px > the 8192 px cap → rejected native-side.
        var act = () => QrCode.GeneratePng("A", moduleSize: 500);

        act.Should().Throw<ImagingException>().WithMessage("*exceeds limit*");
    }
}
