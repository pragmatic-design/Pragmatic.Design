namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     What the RIFF container says about the image to decode: the canvas, whether it has alpha, and where
///     the bitstream of the image (or of an animation's first frame) lies, with its offset on the canvas.
/// </summary>
/// <param name="CanvasWidth">The width of the image as shown.</param>
/// <param name="CanvasHeight">The height of the image as shown.</param>
/// <param name="HasAlpha">Whether the container or the lossless header says the image has alpha.</param>
/// <param name="IsAnimated">Whether the image is an animation, of which only the first frame is decoded.</param>
/// <param name="FrameX">The frame's offset on the canvas, in pixels.</param>
/// <param name="FrameY">The frame's offset on the canvas, in pixels.</param>
/// <param name="Image">The offset and length of the <c>VP8 </c> or <c>VP8L</c> chunk's payload.</param>
/// <param name="IsLossless">Whether <paramref name="Image" /> is a <c>VP8L</c> chunk.</param>
/// <param name="Alpha">The offset and length of the <c>ALPH</c> chunk's payload, or a zero length for none.</param>
internal readonly record struct WebPLayout(
    int CanvasWidth,
    int CanvasHeight,
    bool HasAlpha,
    bool IsAnimated,
    int FrameX,
    int FrameY,
    (int Offset, int Length) Image,
    bool IsLossless,
    (int Offset, int Length) Alpha);
