namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     What the encoder does with each quantised block. A struct implementation is specialised by the
///     JIT, so the walk over the blocks calls it directly.
/// </summary>
internal interface IJpegBlockEmitter
{
    /// <summary>
    ///     One block in the transposed layout, its non-zero mask in zigzag order (bit k for zigzag position k),
    ///     the component's DC predictor, and the component: 0 luma, 1 chroma.
    /// </summary>
    void Emit(ReadOnlySpan<short> block, ulong zigzagMask, ref int predictor, int component);
}
