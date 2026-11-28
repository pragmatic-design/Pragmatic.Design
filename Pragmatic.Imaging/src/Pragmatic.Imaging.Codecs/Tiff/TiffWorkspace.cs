using System.Buffers;

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

    // Kept on the thread up to here; a larger sample buffer comes from the shared pool, so that one large image does
    // not hold its memory on the thread for good.
    private const int KeptBytes = 16 * 1024 * 1024;

    private byte[] _samples = [];
    private byte[] _row = new byte[1024];
    private byte[] _copy = new byte[1024];

    /// <summary>
    ///     A sample buffer of <paramref name="length" /> bytes: the workspace's own, or, above what it keeps, one rented
    ///     from the shared pool and returned through <paramref name="rented" />. The workspace's own never goes through
    ///     the shared pool, whose trimming after a full collection allocates on the finalizer thread.
    /// </summary>
    public Span<byte> Samples(int length, out byte[]? rented)
    {
        rented = null;
        if (length > KeptBytes)
        {
            rented = ArrayPool<byte>.Shared.Rent(length);
            return rented.AsSpan(0, length);
        }

        if (_samples.Length < length)
            _samples = new byte[Math.Max(length, Math.Min(KeptBytes, _samples.Length * 2))];
        return _samples.AsSpan(0, length);
    }

    /// <summary>Scratch for a coded row of <paramref name="length" /> bytes.</summary>
    public Span<byte> Row(int length)
    {
        if (_row.Length < length)
            _row = new byte[Math.Max(length, _row.Length * 2)];
        return _row.AsSpan(0, length);
    }

    /// <summary>Scratch for a copy of a row, apart from <see cref="Row" />.</summary>
    public Span<byte> Copy(int length)
    {
        if (_copy.Length < length)
            _copy = new byte[Math.Max(length, _copy.Length * 2)];
        return _copy.AsSpan(0, length);
    }

    public static TiffWorkspace Take()
    {
        var workspace = _idle ?? new TiffWorkspace();
        _idle = null;
        return workspace;
    }

    public static void Give(TiffWorkspace workspace) => _idle = workspace;
}
