namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     One IFD entry: its field type, its count, and where its 4-byte value field sits in the file. The values are
///     in that field when they fit in it, and at the offset it holds otherwise.
/// </summary>
internal readonly record struct TiffEntry(ushort Type, uint Count, int Field)
{
    public const ushort Byte = 1;
    public const ushort Ascii = 2;
    public const ushort Short = 3;
    public const ushort Long = 4;
    public const ushort Rational = 5;
    public const ushort Undefined = 7;
    public const ushort Ifd = 13;
    public const ushort Long8 = 16;
    public const ushort Ifd8 = 18;

    /// <summary>The bytes of one value of a field type, or 0 for a type the TIFF crate does not know.</summary>
    public static int SizeOf(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _ => 0,
    };

    /// <summary>Where value <paramref name="index" /> starts: inline in the field, or at the offset the field holds.</summary>
    public long At(in TiffReader reader, long index)
    {
        var size = SizeOf(Type);
        return (long)Count * size <= 4 ? Field + index * size : reader.U32(Field) + index * size;
    }
}
