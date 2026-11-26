namespace Pragmatic.Imaging;

/// <summary>How <see cref="ImagePipeline.EncodeWebP" /> writes a WebP file.</summary>
public enum WebPEncoding
{
    /// <summary>VP8L: every pixel decodes back exactly, transparent ones with their colour. What <see cref="ImageFormat.WebP" /> writes.</summary>
    Lossless = 0,

    /// <summary>VP8 at a quality setting, 0 to 100 on libwebp's scale; any alpha is kept exactly beside it.</summary>
    Lossy = 1,
}
