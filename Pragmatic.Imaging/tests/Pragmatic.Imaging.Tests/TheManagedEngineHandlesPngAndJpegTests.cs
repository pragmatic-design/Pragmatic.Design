using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Imaging.Engine;
using Pragmatic.Imaging.Tests.Exif;
using Pragmatic.Testing.Assertions;
using ZXing;
using ZXing.Common;
using CoreQr = Pragmatic.Imaging.Codecs.Barcodes.QrCode;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     PNG and JPEG go through the managed core, not the native library: what the module writes is what
///     the core's encoders write, what it reads is what the core's decoder reads, and a QR code is the
///     core's matrix. Each assertion is on the output, which differs between the two engines, so it
///     cannot pass on the native path.
/// </summary>
public class TheManagedEngineHandlesPngAndJpegTests
{
    [Fact]
    public void A_png_encode_is_the_managed_encoders_output()
    {
        using var source = OrientedImages.Pixels();
        using var reference = new MemoryStream();
        PngEncoder.Write(source, reference, ImageEncoding.PngCompression);

        using var pipeline = ImagePipeline.Load(reference.ToArray());
        var encoded = pipeline.Encode(ImageFormat.Png);

        encoded.Should().Equal(reference.ToArray());
    }

    [Fact]
    public void A_jpeg_encode_is_the_managed_encoders_output()
    {
        using var source = OrientedImages.Pixels();
        using var png = new MemoryStream();
        PngEncoder.Write(source, png);
        using var reference = new MemoryStream();
        JpegEncoder.Write(source, reference, quality: 80);

        using var pipeline = ImagePipeline.Load(png.ToArray());
        var encoded = pipeline.Encode(ImageFormat.Jpeg, quality: 80);

        encoded.Should().Equal(reference.ToArray());
    }

    [Fact]
    public void A_jpeg_decode_is_the_managed_decoders_pixels()
    {
        var jpeg = OrientedImages.Jpeg(1);
        using var reference = JpegDecoder.Decode(jpeg);

        using var pipeline = ImagePipeline.Load(jpeg);
        using var decoded = PngDecoder.Decode(pipeline.Encode(ImageFormat.Png));

        decoded.Pixels.ToArray().Should().Equal(reference.Pixels.ToArray());
    }

    [Fact]
    public void A_qr_code_is_the_managed_matrix_and_reads_back()
    {
        // Digits only: a native encoder picks numeric mode, the core byte mode, so the matrices differ.
        const string text = "0123456789012345678901234567890123456789";
        const uint moduleSize = 4;
        const uint margin = 4;
        var matrix = CoreQr.Encode(text);

        var png = QrCode.GeneratePng(text, moduleSize, margin);

        using var image = PngDecoder.Decode(png);
        image.Width.Should().Be((int)((matrix.Size + 2 * margin) * moduleSize));
        for (var y = 0; y < matrix.Size; y++)
        {
            for (var x = 0; x < matrix.Size; x++)
            {
                var px = (int)((x + margin) * moduleSize + moduleSize / 2);
                var py = (int)((y + margin) * moduleSize + moduleSize / 2);
                var dark = image.Row(py)[px * 4] < 128;
                dark.Should().Be(matrix[x, y]);
            }
        }

        var reader = new BarcodeReaderGeneric { Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], PureBarcode = true } };
        var result = reader.Decode(new RGBLuminanceSource(image.Pixels.ToArray(), image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32));
        result.Should().NotBeNull();
        result!.Text.Should().Be(text);
    }
}
