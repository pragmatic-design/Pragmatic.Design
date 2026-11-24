namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>The seven passes of Adam7: where each starts and how far it steps, in both directions.</summary>
internal readonly record struct PngInterlace(int XStart, int YStart, int XStep, int YStep)
{
    public static readonly PngInterlace[] Passes =
    [
        new(0, 0, 8, 8),
        new(4, 0, 8, 8),
        new(0, 4, 4, 8),
        new(2, 0, 4, 4),
        new(0, 2, 2, 4),
        new(1, 0, 2, 2),
        new(0, 1, 1, 2),
    ];

    /// <summary>How many pixels of a row of <paramref name="width" /> this pass holds; zero when the image is too narrow for it.</summary>
    public int PassWidth(int width) => width <= XStart ? 0 : (width - XStart + XStep - 1) / XStep;

    public int PassHeight(int height) => height <= YStart ? 0 : (height - YStart + YStep - 1) / YStep;
}
