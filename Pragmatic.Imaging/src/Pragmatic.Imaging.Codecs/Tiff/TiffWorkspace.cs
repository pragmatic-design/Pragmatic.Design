namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     What a TIFF decode works with, kept on the thread from one decode to the next: the directory, the image and
///     its chunk lists, the sources of each compression with their tables, so a decode allocates its pixels and little
///     else. Taken for a decode and given back after it.
/// </summary>
internal sealed class TiffWorkspace
{
    [ThreadStatic]
    private static TiffWorkspace? _idle;

    public TiffDirectory Directory { get; } = new();
    public TiffImage Image { get; } = new();

    /// <summary>Scratch for a tag's values.</summary>
    public ulong[] Values = new ulong[8];

    public ushort[] Shorts = new ushort[8];
    public TiffLzwSource Lzw { get; } = new();
    public TiffPackBitsSource PackBits { get; } = new();
    public TiffMemorySource Memory { get; } = new();
    public TiffFax4Source Fax { get; } = new();

    public static TiffWorkspace Take()
    {
        var workspace = _idle ?? new TiffWorkspace();
        _idle = null;
        return workspace;
    }

    public static void Give(TiffWorkspace workspace) => _idle = workspace;
}
