namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     Hand-made GIF files for what Pillow does not write: a first frame off the logical screen's origin, past its
///     edge, with indices past its colour table, and the malformed files a decoder must refuse. The image data is
///     a clear code, the indices as literal codes, and an end code, at the code sizes a decoder expects; a test can
///     put any code in, or cut the data short.
/// </summary>
internal static class GifStreams
{
    /// <summary>One frame of a file, and the parts of the file around it a test can change.</summary>
    internal sealed record Frame(int Left, int Top, int Width, int Height, byte[] Indices)
    {
        public bool Interlaced { get; init; }

        /// <summary>The local colour table as R, G, B, or none.</summary>
        public byte[]? LocalTable { get; init; }

        public int MinimumCodeSize { get; init; } = 2;

        /// <summary>Codes put after the indices, before the end code; a negative one stands for the end code itself.</summary>
        public int[] ExtraCodes { get; init; } = [];

        public bool OmitClear { get; init; }

        public bool OmitEnd { get; init; }

        /// <summary>How many bytes of the image data to keep, all of them when null; the file ends there.</summary>
        public int? CutDataAt { get; init; }
    }

    /// <summary>A GIF89a with a global table (or none), the extensions as given, one frame and a trailer.</summary>
    public static byte[] File(int width, int height, byte[]? globalTable, Frame frame, params byte[][] extensions)
    {
        var file = new List<byte>();
        file.AddRange("GIF89a"u8.ToArray());
        file.AddRange([(byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)]);
        file.Add(globalTable is null ? (byte)0 : (byte)(0x80 | TableBits(globalTable)));
        file.AddRange([0, 0]);
        if (globalTable is not null)
            file.AddRange(Padded(globalTable));
        foreach (var extension in extensions)
            file.AddRange(extension);

        file.Add(0x2C);
        file.AddRange([(byte)frame.Left, (byte)(frame.Left >> 8), (byte)frame.Top, (byte)(frame.Top >> 8)]);
        file.AddRange([(byte)frame.Width, (byte)(frame.Width >> 8), (byte)frame.Height, (byte)(frame.Height >> 8)]);
        var flags = (byte)((frame.Interlaced ? 0x40 : 0) | (frame.LocalTable is null ? 0 : 0x80 | TableBits(frame.LocalTable)));
        file.Add(flags);
        if (frame.LocalTable is not null)
            file.AddRange(Padded(frame.LocalTable));
        file.Add((byte)frame.MinimumCodeSize);

        var data = Lzw(frame);
        var blocks = new List<byte>();
        for (var at = 0; at < data.Length; at += 255)
        {
            var length = Math.Min(255, data.Length - at);
            blocks.Add((byte)length);
            blocks.AddRange(data.AsSpan(at, length).ToArray());
        }

        blocks.Add(0);
        if (frame.CutDataAt is { } cut)
        {
            file.AddRange(blocks.Take(cut));
            return [.. file];
        }

        file.AddRange(blocks);
        file.Add(0x3B);
        return [.. file];
    }

    /// <summary>A graphic control extension: its data sub-block of <paramref name="length" /> bytes (4 is right).</summary>
    public static byte[] Control(int? transparent, int length = 4)
    {
        var data = new byte[length];
        if (length > 0)
            data[0] = (byte)(transparent is null ? 0 : 1);
        if (length > 3)
            data[3] = (byte)(transparent ?? 0);
        return [0x21, 0xF9, (byte)length, .. data, 0];
    }

    public static byte[] Extension(byte label, params byte[] data) => [0x21, label, (byte)data.Length, .. data, 0];

    private static byte[] Lzw(Frame frame)
    {
        var size = frame.MinimumCodeSize;
        var clear = 1 << size;
        var end = clear + 1;
        var codeSize = size + 1;
        var next = end + 1;
        var first = true;
        uint bits = 0;
        var held = 0;
        var output = new List<byte>();

        void Write(int code)
        {
            bits |= (uint)code << held;
            held += codeSize;
            while (held >= 8)
            {
                output.Add((byte)bits);
                bits >>= 8;
                held -= 8;
            }
        }

        // A code as the decoder takes it: every one but the first after a clear defines an entry.
        void Code(int code)
        {
            Write(code);
            if (first)
            {
                first = false;
                return;
            }

            if (next < 4096)
            {
                if (next == (1 << codeSize) - 1 && codeSize < 12)
                    codeSize++;
                next++;
            }
        }

        if (!frame.OmitClear)
            Write(clear);
        foreach (var index in frame.Indices)
            Code(index);
        foreach (var extra in frame.ExtraCodes)
            Code(extra < 0 ? end : extra);
        if (!frame.OmitEnd)
            Write(end);
        if (held > 0)
            output.Add((byte)bits);
        return [.. output];
    }

    private static int TableBits(byte[] table)
    {
        var entries = table.Length / 3;
        var bits = 0;
        while (2 << bits < entries)
            bits++;
        return bits;
    }

    private static byte[] Padded(byte[] table)
    {
        var entries = 2 << TableBits(table);
        var padded = new byte[entries * 3];
        table.CopyTo(padded, 0);
        return padded;
    }
}
