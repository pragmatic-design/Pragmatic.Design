using Pragmatic.Imaging.Codecs.Barcodes;
using Xunit;
using ZXing;
using ZXing.Common;

namespace Pragmatic.Imaging.Codecs.Tests.Barcodes;

/// <summary>
///     The barcode encoders: their tables hold the invariants the standards fix, and what they produce,
///     drawn as pixels, is what an independent decoder (ZXing.Net) reads back.
/// </summary>
public class BarcodeTests
{
    [Fact]
    public void The_qr_block_table_matches_the_symbols_capacity_at_every_version()
    {
        for (var version = 1; version <= 40; version++)
        {
            var (ec, blocks1, data1, blocks2, data2) = QrTables.Blocks(version);
            var total = blocks1 * (data1 + ec) + blocks2 * (data2 + ec);
            Assert.Equal(QrTables.TotalCodewords(version), total);
        }
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("https://pragmaticdesign.net", 3)] // 27 bytes: one too many for version 2 at level M
    [InlineData("The quick brown fox jumps over the lazy dog, again and again and again, until the symbol grows.", 6)]
    public void A_qr_code_takes_the_smallest_version_that_holds_the_text(string text, int version)
    {
        var qr = QrCode.Encode(text);
        Assert.Equal(version, qr.Version);
        Assert.Equal(17 + 4 * version, qr.Size);
        // The finder pattern's dark centre and light ring, top left.
        Assert.True(qr[3, 3]);
        Assert.False(qr[1, 1]);
        Assert.True(qr[0, 0]);
    }

    [Fact]
    public void The_code128_patterns_have_the_standard_s_widths_and_are_all_distinct()
    {
        var patterns = LinearBarcodes.Code128Patterns;
        Assert.Equal(107, patterns.Length);
        for (var i = 0; i < 106; i++)
        {
            Assert.Equal(6, patterns[i].Length);
            Assert.Equal(11, patterns[i].Sum(c => c - '0'));
        }

        Assert.Equal(13, patterns[106].Sum(c => c - '0'));
        Assert.Equal(107, patterns.Distinct().Count());
    }

    [Fact]
    public void The_code39_patterns_have_nine_elements_three_of_them_wide()
    {
        foreach (var c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%")
        {
            var widths = LinearBarcodes.Code39(c.ToString());
            Assert.Equal(29, widths.Length); // * c * with two gaps: 9 + 1 + 9 + 1 + 9
            Assert.Equal(3, widths.Take(9).Count(w => w == 3));
        }
    }

    [Fact]
    public void Ean_check_digits_are_computed_and_verified()
    {
        Assert.Equal(LinearBarcodes.Ean13("400638133393"), LinearBarcodes.Ean13("4006381333931"));
        Assert.Throws<ArgumentException>(() => LinearBarcodes.Ean13("4006381333930"));
        Assert.Equal(LinearBarcodes.Ean8("9638507"), LinearBarcodes.Ean8("96385074"));
        Assert.Equal(95, LinearBarcodes.Ean13("4006381333931").Sum()); // 3 + 6×7 + 5 + 6×7 + 3 modules
        Assert.Equal(67, LinearBarcodes.Ean8("96385074").Sum());
    }

    [Theory]
    [InlineData("https://pragmaticdesign.net/orders?id=42")]
    [InlineData("A")]
    [InlineData("Città, perché: UTF-8 bytes")]
    public void A_qr_code_is_read_back_by_an_independent_decoder(string text)
    {
        Assert.Equal(text, Read(BarcodeRaster.Qr(QrCode.Encode(text)), BarcodeFormat.QR_CODE));
    }

    [Fact]
    public void A_long_qr_code_with_two_block_groups_is_read_back_too()
    {
        var text = string.Concat(Enumerable.Repeat("Pragmatic.Imaging ", 40)).Trim();
        var qr = QrCode.Encode(text);
        Assert.True(qr.Version >= 10, "the text should need a version with two block groups");
        Assert.Equal(text, Read(BarcodeRaster.Qr(qr), BarcodeFormat.QR_CODE));
    }

    [Theory]
    [InlineData("ORDER-2026-000042")]
    [InlineData("1234567890")] // set C, digits in pairs
    [InlineData("AB12345678CD")] // set B, then C, then B
    public void A_code128_is_read_back_by_an_independent_decoder(string text)
    {
        Assert.Equal(text, Read(BarcodeRaster.Linear(LinearBarcodes.Code128(text)), BarcodeFormat.CODE_128));
    }

    [Fact]
    public void A_code39_is_read_back_by_an_independent_decoder()
    {
        Assert.Equal("HELLO 39", Read(BarcodeRaster.Linear(LinearBarcodes.Code39("HELLO 39")), BarcodeFormat.CODE_39));
    }

    [Theory]
    [InlineData("4006381333931", BarcodeFormat.EAN_13)]
    [InlineData("96385074", BarcodeFormat.EAN_8)]
    public void An_ean_is_read_back_by_an_independent_decoder(string digits, BarcodeFormat format)
    {
        var widths = format == BarcodeFormat.EAN_13 ? LinearBarcodes.Ean13(digits) : LinearBarcodes.Ean8(digits);
        Assert.Equal(digits, Read(BarcodeRaster.Linear(widths), format));
    }

    private static string? Read(BarcodeRaster.Image image, BarcodeFormat format)
    {
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions { PossibleFormats = [format], TryHarder = true, PureBarcode = false },
            AutoRotate = false,
        };
        var source = new RGBLuminanceSource(image.Rgba, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32);
        return reader.Decode(source)?.Text;
    }
}
