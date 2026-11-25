using System.Buffers.Binary;
using Pragmatic.Imaging.Codecs.Jpeg;

namespace Pragmatic.Imaging.Codecs.Tests.Metadata;

/// <summary>Small JPEGs from the managed encoder, with marker segments inserted where a test needs them.</summary>
internal static class Jpegs
{
    /// <summary>A 16×8 gradient: SOI, then the encoder's own segments (JFIF APP0 first).</summary>
    public static byte[] Plain()
    {
        using var image = new PixelBuffer(16, 8);
        var pixels = image.Pixels;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i % 251);
            pixels[i + 1] = (byte)(i / 4 % 16 * 16);
            pixels[i + 2] = 90;
            pixels[i + 3] = 255;
        }

        using var output = new MemoryStream();
        JpegEncoder.Write(image, output, quality: 90);
        return output.ToArray();
    }

    /// <summary><see cref="Plain" /> with one APP1 segment right after SOI.</summary>
    public static byte[] WithApp1(byte[] payload) => Insert(Plain(), 2, Segment(0xE1, payload));

    public static byte[] Segment(byte marker, ReadOnlySpan<byte> payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(2 + payload.Length));
        payload.CopyTo(segment.AsSpan(4));
        return segment;
    }

    public static byte[] Insert(byte[] jpeg, int at, byte[] segment) => [.. jpeg.AsSpan(0, at), .. segment, .. jpeg.AsSpan(at)];

    /// <summary>The length of the segment starting at <paramref name="at" />, marker included.</summary>
    public static int SegmentLength(byte[] jpeg, int at) => 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
}
