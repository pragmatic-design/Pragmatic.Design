using System.Text;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     A tag's values as the TIFF crate 0.11 converts them for the native library: which field types each
///     conversion accepts, and that a value too large for the asked width is an error, not a truncation.
/// </summary>
internal static class TiffValues
{
    /// <summary>A single unsigned value of at most <paramref name="max" />; IFD pointers count as values unless <paramref name="pointers" /> is false.</summary>
    public static ulong Scalar(in TiffReader reader, TiffEntry entry, ulong max, bool pointers = true)
    {
        if (entry.Count != 1)
            throw Invalid();
        var value = entry.Type switch
        {
            TiffEntry.Byte or TiffEntry.Undefined or TiffEntry.Short or TiffEntry.Long or TiffEntry.Long8 => Element(reader, entry, 0),
            TiffEntry.Ifd or TiffEntry.Ifd8 when pointers => Element(reader, entry, 0),
            _ => throw Invalid(),
        };
        return value <= max ? value : throw Invalid();
    }

    /// <summary>Every value widened to 64 bits: integers, both halves of one rational, the characters of a string.</summary>
    /// <returns>How many values were written to <paramref name="values" />, which grows to hold them.</returns>
    public static int Unsigned(in TiffReader reader, TiffEntry entry, ref ulong[] values)
    {
        if (entry.Count == 0)
            return 0;
        if (entry.Type == TiffEntry.Ascii)
        {
            var count = 0;
            foreach (var rune in Characters(reader, entry).EnumerateRunes())
            {
                Grow(ref values, count + 1);
                values[count++] = (ulong)rune.Value;
            }

            return count;
        }

        if (entry.Count == 1 && entry.Type == TiffEntry.Rational)
        {
            var at = entry.At(reader, 0);
            Grow(ref values, 2);
            values[0] = reader.U32(at);
            values[1] = reader.U32(at + 4);
            return 2;
        }

        if (entry.Type is not (TiffEntry.Byte or TiffEntry.Undefined or TiffEntry.Short or TiffEntry.Long or TiffEntry.Long8 or TiffEntry.Ifd
            or TiffEntry.Ifd8))
            throw Invalid();
        Available(reader, entry);
        Grow(ref values, (int)entry.Count);
        for (var i = 0; i < entry.Count; i++)
            values[i] = Element(reader, entry, i);
        return (int)entry.Count;
    }

    /// <summary>Every value as 16 bits: a single value only as a byte or a short, a list of any unsigned width that fits.</summary>
    /// <returns>How many values were written to <paramref name="values" />, which grows to hold them.</returns>
    public static int Shorts(in TiffReader reader, TiffEntry entry, ref ushort[] values)
    {
        if (entry.Count == 0)
            return 0;
        var narrow = entry.Type is TiffEntry.Byte or TiffEntry.Undefined or TiffEntry.Short;
        if (!narrow && (entry.Count == 1 || entry.Type is not (TiffEntry.Long or TiffEntry.Long8)))
            throw Invalid();
        Available(reader, entry);
        if (values.Length < entry.Count)
            values = new ushort[Math.Max(entry.Count, values.Length * 2)];
        for (var i = 0; i < entry.Count; i++)
        {
            var value = Element(reader, entry, i);
            values[i] = value <= ushort.MaxValue ? (ushort)value : throw Invalid();
        }

        return (int)entry.Count;
    }

    public static void Grow(ref ulong[] values, int count)
    {
        if (values.Length < count)
            values = new ulong[Math.Max(count, values.Length * 2)];
    }

    // Every value is in the file, before anything is allocated for them.
    private static void Available(in TiffReader reader, TiffEntry entry) =>
        reader.Bytes(entry.At(reader, 0), (long)entry.Count * TiffEntry.SizeOf(entry.Type));

    /// <summary>The bytes of a BYTE or UNDEFINED value.</summary>
    public static ReadOnlySpan<byte> Bytes(in TiffReader reader, TiffEntry entry)
    {
        if (entry.Count == 0)
            return [];
        if (entry.Type is not (TiffEntry.Byte or TiffEntry.Undefined))
            throw Invalid();
        return reader.Bytes(entry.At(reader, 0), entry.Count);
    }

    private static ulong Element(in TiffReader reader, TiffEntry entry, long index)
    {
        var at = entry.At(reader, index);
        return entry.Type switch
        {
            TiffEntry.Byte or TiffEntry.Undefined => reader.Bytes(at, 1)[0],
            TiffEntry.Short => reader.U16(at),
            TiffEntry.Long or TiffEntry.Ifd => reader.U32(at),
            _ => reader.U64(at),
        };
    }

    // A one-character string must be the empty one; an inline string must be ASCII and end in a NUL, trimmed of NULs
    // at both ends; one stored elsewhere ends at its first NUL and must be UTF-8.
    private static string Characters(in TiffReader reader, TiffEntry entry)
    {
        var bytes = reader.Bytes(entry.At(reader, 0), entry.Count);
        if (entry.Count == 1)
            return bytes[0] == 0 ? "" : throw Invalid();
        if (entry.Count <= 4)
        {
            if (bytes[^1] != 0 || !Ascii.IsValid(bytes))
                throw Invalid();
            return Encoding.ASCII.GetString(bytes).Trim('\0');
        }

        var end = bytes.IndexOf((byte)0);
        if (end >= 0)
            bytes = bytes[..end];
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static ImageDecodeException Invalid() => new("TIFF: a tag has a value of the wrong type or out of range.");
}
