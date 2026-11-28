using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     How the encoder writes a buffer of any format but 8-bit RGBA, which it handles itself: the colour type and depth
///     the file takes, and each row as the file's big-endian samples. A format of 16 bits or float is written at 16 bits,
///     so nothing the pipeline kept is dropped at the end; RGBA whose every alpha is opaque is written as RGB.
/// </summary>
internal readonly record struct PngRowLayout(PngColorType ColorType, int BitDepth, int BytesPerPixel)
{
    public static PngRowLayout Of(PixelBuffer image) => image.Format switch
    {
        PixelFormat.Rgba16 or PixelFormat.RgbaFloat => IsOpaque(image) ? new(PngColorType.Rgb, 16, 6) : new(PngColorType.Rgba, 16, 8),
        PixelFormat.Rgb16 => new(PngColorType.Rgb, 16, 6),
        PixelFormat.Gray16 => new(PngColorType.Gray, 16, 2),
        PixelFormat.Rgb8 => new(PngColorType.Rgb, 8, 3),
        PixelFormat.Gray8 => new(PngColorType.Gray, 8, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(image), image.Format, "8-bit RGBA is the encoder's own path."),
    };

    /// <summary>Row <paramref name="y" /> of <paramref name="image" /> as the file stores it, into <paramref name="raw" />.</summary>
    public void Fill(PixelBuffer image, int y, Span<byte> raw)
    {
        var row = image.Row(y);
        switch (image.Format)
        {
            case PixelFormat.Rgb8 or PixelFormat.Gray8:
                row.CopyTo(raw);
                return;
            case PixelFormat.Rgb16 or PixelFormat.Gray16:
                var samples = MemoryMarshal.Cast<byte, ushort>(row);
                for (var i = 0; i < samples.Length; i++)
                    BinaryPrimitives.WriteUInt16BigEndian(raw[(i * 2)..], samples[i]);
                return;
        }

        var channels = BytesPerPixel / 2;
        for (var x = 0; x < image.Width; x++)
        {
            for (var c = 0; c < channels; c++)
                BinaryPrimitives.WriteUInt16BigEndian(raw[((x * channels + c) * 2)..], Sample(image, row, x * 4 + c));
        }
    }

    // One channel of an RGBA16 or float row, as a 16-bit sample.
    private static ushort Sample(PixelBuffer image, ReadOnlySpan<byte> row, int index) => image.Format == PixelFormat.Rgba16
        ? MemoryMarshal.Cast<byte, ushort>(row)[index]
        : (ushort)MathF.Round(PixelConversion.UnitClamp(MemoryMarshal.Cast<byte, float>(row)[index]) * 65535f, MidpointRounding.AwayFromZero);

    private static bool IsOpaque(PixelBuffer image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);
            for (var x = 0; x < image.Width; x++)
            {
                if (Sample(image, row, x * 4 + 3) != ushort.MaxValue)
                    return false;
            }
        }

        return true;
    }
}
