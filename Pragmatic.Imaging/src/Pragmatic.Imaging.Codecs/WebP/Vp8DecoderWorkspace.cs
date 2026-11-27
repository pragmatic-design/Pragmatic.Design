namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What the lossy decoder works in, kept from one decode to the next on the thread that ran it, so that a
///     decode allocates nothing but the image it returns: the native decoder allocates only that wrapper on the
///     managed heap. What depends on the image (the planes, the rows as wide as the frame, the filter strength
///     of every macroblock) still comes from the shared array pool each time and goes back at the end.
/// </summary>
internal sealed class Vp8DecoderWorkspace
{
    /// <summary>The most token partitions a frame has (RFC 6386, section 9.5).</summary>
    public const int MaxPartitions = 8;

    [ThreadStatic]
    private static Vp8DecoderWorkspace? _idle;

    private Vp8DecoderWorkspace()
    {
        for (var p = 0; p < MaxPartitions; p++)
            Partitions[p] = new Vp8BoolDecoder();
    }

    /// <summary>The first partition: the header, then the modes of every macroblock.</summary>
    public Vp8BoolDecoder ModesReader { get; } = new();

    /// <summary>The token partitions; a frame uses the first <see cref="Vp8FrameHeader.Partitions" />.</summary>
    public Vp8BoolDecoder[] Partitions { get; } = new Vp8BoolDecoder[MaxPartitions];

    public Vp8FrameHeader Header { get; } = new();

    /// <summary>The loop filter's strengths per segment, without and with subblock prediction.</summary>
    public Vp8FilterStrength[] Strengths { get; } = new Vp8FilterStrength[Vp8FrameHeader.Segments * 2];

    public Vp8Frame Frame { get; } = new();

    public Vp8Reconstructor Reconstructor { get; } = new();

    public Vp8MacroblockModes Modes { get; } = new();

    /// <summary>The thread's idle workspace, or a new one if it has none.</summary>
    public static Vp8DecoderWorkspace Take()
    {
        var workspace = _idle ?? new Vp8DecoderWorkspace();
        _idle = null;
        return workspace;
    }

    /// <summary>
    ///     Gives the frame's planes back to the pool and keeps <paramref name="workspace" /> for the thread's
    ///     next decode. A decode that throws does not give its workspace back: the next one starts from a new
    ///     one rather than from state left halfway.
    /// </summary>
    public static void Give(Vp8DecoderWorkspace workspace)
    {
        workspace.Frame.Dispose();
        _idle = workspace;
    }
}
