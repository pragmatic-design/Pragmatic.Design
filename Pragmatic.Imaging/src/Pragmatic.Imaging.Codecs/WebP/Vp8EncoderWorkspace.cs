namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What the lossy encoder works in, kept from one encode to the next on the thread that ran it, so that an
///     encode allocates nothing but the file it returns: libwebp's own encode allocates only that, as far as
///     the managed heap can see. What depends on the image (the planes, the tokens, the rows as wide as the
///     picture, the partitions) still comes from the shared array pool each time and goes back at the end.
/// </summary>
internal sealed class Vp8EncoderWorkspace
{
    [ThreadStatic]
    private static Vp8EncoderWorkspace? _idle;

    private Vp8EncoderWorkspace() => Coder = new Vp8MacroblockCoder(Costs);

    public Vp8Picture Picture { get; } = new();

    public Vp8Analysis Analysis { get; } = new();

    public Vp8SegmentPlan Plan { get; } = new();

    public Vp8Tokens Tokens { get; } = new();

    public Vp8EncodedModes Modes { get; } = new();

    public Vp8Costs Costs { get; } = new();

    public Vp8MacroblockCoder Coder { get; }

    public Vp8MacroblockDecision Decision { get; } = new();

    public Vp8EncodedFrame Frame { get; } = new();

    /// <summary>The thread's idle workspace, or a new one if it has none.</summary>
    public static Vp8EncoderWorkspace Take()
    {
        var workspace = _idle ?? new Vp8EncoderWorkspace();
        _idle = null;
        return workspace;
    }

    /// <summary>
    ///     Gives the image's buffers back to the pool and keeps <paramref name="workspace" /> for the thread's
    ///     next encode. An encode that throws does not give its workspace back: the next one starts from a new
    ///     one rather than from state left halfway.
    /// </summary>
    public static void Give(Vp8EncoderWorkspace workspace)
    {
        workspace.ReleaseFrame();
        workspace.Picture.Release();
        _idle = workspace;
    }

    /// <summary>Gives back what one attempt at the frame took from the pool; the picture stays.</summary>
    public void ReleaseFrame()
    {
        Analysis.Release();
        Tokens.Release();
        Modes.Release();
        Coder.Release();
        Frame.Release();
    }
}
