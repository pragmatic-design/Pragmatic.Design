namespace Pragmatic.Imaging.Codecs;

/// <summary>
///     An encoder's view of its input as 8-bit RGBA: the buffer itself when it already is, otherwise the conversion
///     <see cref="PixelConversion.ToRgba8" /> made, freed when the lease is disposed.
/// </summary>
internal readonly ref struct Rgba8Lease
{
    private readonly bool _owned;

    public Rgba8Lease(PixelBuffer image)
    {
        Image = PixelConversion.ToRgba8(image);
        _owned = !ReferenceEquals(Image, image);
    }

    public PixelBuffer Image { get; }

    public void Dispose()
    {
        if (_owned)
            Image.Dispose();
    }
}
