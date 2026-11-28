using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Png;
using Pragmatic.Imaging.Native;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     Every pipeline operation gives what the native library gives for the same call on the same pixels:
///     exactly, for the operations that move or map pixels; within one unit per channel for blur and
///     sharpen, whose sums are formed in a different order. The source is 61×47 (odd, so no operation
///     gets an even-sized shortcut) with noise and a varying alpha, so edges and alpha are exercised. Blur
///     and sharpen are compared on the same image made opaque: on translucent pixels they work
///     premultiplied, which the native library does not, and differ from it on purpose
///     (<see cref="ABlurredTransparentImageHasNoDarkFringeTests" />).
/// </summary>
public unsafe class TheOperationsMatchTheNativeLibraryTests
{
    private const int Width = 61;
    private const int Height = 47;

    public static TheoryData<string> ExactOperations =>
    [
        "rotate90", "rotate180", "rotate270", "flipH", "flipV", "crop",
        "grayscale", "brightness+40", "brightness-70", "contrast+30", "contrast-50",
    ];

    public static TheoryData<string> FilterOperations => ["blur0", "blur1.5", "blur3", "sharpen1", "sharpen2.5-t4"];

    [Theory]
    [MemberData(nameof(ExactOperations))]
    public void An_operation_that_maps_pixels_matches_exactly(string operation)
    {
        var (managed, native) = Run(operation);

        managed.Width.Should().Be(native.Width);
        managed.Height.Should().Be(native.Height);
        managed.Rgba.Should().Equal(native.Rgba);
    }

    [Theory]
    [MemberData(nameof(FilterOperations))]
    public void A_filter_matches_within_one_unit_per_channel(string operation)
    {
        var (managed, native) = Run(operation, opaque: true);

        managed.Rgba.Length.Should().Be(native.Rgba.Length);
        var worst = 0;
        for (var i = 0; i < managed.Rgba.Length; i++)
            worst = Math.Max(worst, Math.Abs(managed.Rgba[i] - native.Rgba[i]));
        worst.Should().BeLessThanOrEqualTo(1);
    }

    private static (NativeReference.Image Managed, NativeReference.Image Native) Run(string operation, bool opaque = false)
    {
        var source = Source(opaque);
        var png = Encode(source);
        using var pipeline = ImagePipeline.Load(png);

        NativeReference.Image native;
        switch (operation)
        {
            case "rotate90" or "rotate180" or "rotate270":
                var degrees = int.Parse(operation["rotate".Length..], System.Globalization.CultureInfo.InvariantCulture);
                pipeline.Rotate(degrees);
                native = NativeReference.Rotate(source, degrees);
                break;
            case "flipH":
                pipeline.FlipHorizontal();
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_flip(p, l, w, h, 1, o, ol));
                break;
            case "flipV":
                pipeline.FlipVertical();
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_flip(p, l, w, h, 0, o, ol));
                break;
            case "crop":
                pipeline.Crop(7, 3, 40, 29);
                native = NativeReference.Crop(source, 7, 3, 40, 29);
                break;
            case "grayscale":
                pipeline.Grayscale();
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_grayscale(p, l, w, h, o, ol));
                break;
            case "brightness+40":
                pipeline.Brightness(40);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_brightness(p, l, w, h, 40, o, ol));
                break;
            case "brightness-70":
                pipeline.Brightness(-70);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_brightness(p, l, w, h, -70, o, ol));
                break;
            case "contrast+30":
                pipeline.Contrast(30f);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_contrast(p, l, w, h, 30f, o, ol));
                break;
            case "contrast-50":
                pipeline.Contrast(-50f);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_contrast(p, l, w, h, -50f, o, ol));
                break;
            case "blur0":
                pipeline.Blur(0f);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_blur(p, l, w, h, 0f, o, ol));
                break;
            case "blur1.5":
                pipeline.Blur(1.5f);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_blur(p, l, w, h, 1.5f, o, ol));
                break;
            case "blur3":
                pipeline.Blur(3f);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_blur(p, l, w, h, 3f, o, ol));
                break;
            case "sharpen1":
                pipeline.Sharpen();
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_sharpen(p, l, w, h, 1f, 1, o, ol));
                break;
            case "sharpen2.5-t4":
                pipeline.Sharpen(2.5f, 4);
                native = NativeReference.Apply(source, static (p, l, w, h, o, ol) => NativeImports.pragmatic_image_sharpen(p, l, w, h, 2.5f, 4, o, ol));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }

        using var result = PngDecoder.Decode(pipeline.Encode(ImageFormat.Png));
        return (new NativeReference.Image(result.Pixels.ToArray(), result.Width, result.Height), native);
    }

    private static NativeReference.Image Source(bool opaque)
    {
        var random = new Random(42);
        var rgba = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 4;
                rgba[i] = (byte)Math.Clamp(x * 4 + random.Next(-20, 21), 0, 255);
                rgba[i + 1] = (byte)Math.Clamp(y * 5 + random.Next(-20, 21), 0, 255);
                rgba[i + 2] = (byte)random.Next(256);
                rgba[i + 3] = opaque ? (byte)255 : (byte)(x % 5 == 0 ? 255 : 128 + (x * y) % 128);
            }
        }

        return new NativeReference.Image(rgba, Width, Height);
    }

    private static byte[] Encode(NativeReference.Image image)
    {
        using var buffer = new PixelBuffer(image.Width, image.Height);
        image.Rgba.CopyTo(buffer.Pixels);
        using var output = new MemoryStream();
        PngEncoder.Write(buffer, output);
        return output.ToArray();
    }
}
