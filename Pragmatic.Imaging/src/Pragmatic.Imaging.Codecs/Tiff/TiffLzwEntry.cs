namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     One entry of the LZW table, its word kept as Wuffs keeps it: the last bytes in a block of up to eight, and the
///     entry whose word is everything before that block, a whole number of blocks long. A word is spelled a block at a
///     time; a word of eight bytes or fewer is one block, with no entry to follow.
/// </summary>
internal struct TiffLzwEntry
{
    /// <summary>The word's last (length - 1) % 8 + 1 bytes, the first of them in the low byte.</summary>
    public ulong Block;

    /// <summary>The entry holding the blocks before this one, or -1.</summary>
    public short Prefix;

    public short Depth;
    public byte First;
}
