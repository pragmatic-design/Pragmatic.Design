namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     An image file directory: its entries by tag and the offset of the next one. As the TIFF crate reads it, an
///     entry of a field type it does not know is skipped, and of two entries with the same tag the later one counts.
///     Read again for each file, into the same arrays.
/// </summary>
internal sealed class TiffDirectory
{
    private ushort[] _tags = new ushort[32];
    private TiffEntry[] _entries = new TiffEntry[32];
    private int _count;

    /// <summary>The offset of the next directory, 0 for none.</summary>
    public uint Next { get; private set; }

    public void Read(in TiffReader reader, long at)
    {
        _count = 0;
        var count = reader.U16(at);
        var position = at + 2;
        for (var i = 0; i < count; i++, position += 12)
        {
            var tag = reader.U16(position);
            var type = reader.U16(position + 2);
            var entryCount = reader.U32(position + 4);
            reader.Bytes(position + 8, 4);
            if (TiffEntry.SizeOf(type) != 0)
                Set(tag, new TiffEntry(type, entryCount, (int)(position + 8)));
        }

        Next = reader.U32(position);
    }

    public bool Contains(ushort tag) => IndexOf(tag) >= 0;

    public bool TryGet(ushort tag, out TiffEntry entry)
    {
        var index = IndexOf(tag);
        entry = index >= 0 ? _entries[index] : default;
        return index >= 0;
    }

    private void Set(ushort tag, TiffEntry entry)
    {
        var index = IndexOf(tag);
        if (index >= 0)
        {
            _entries[index] = entry;
            return;
        }

        if (_count == _tags.Length)
        {
            Array.Resize(ref _tags, _count * 2);
            Array.Resize(ref _entries, _count * 2);
        }

        _tags[_count] = tag;
        _entries[_count++] = entry;
    }

    private int IndexOf(ushort tag) => Array.IndexOf(_tags, tag, 0, _count);
}
