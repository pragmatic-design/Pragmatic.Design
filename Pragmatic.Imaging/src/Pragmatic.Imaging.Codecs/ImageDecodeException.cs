namespace Pragmatic.Imaging.Codecs;

/// <summary>Encoded bytes that are not the image they claim to be: truncated, corrupt, or of a shape not supported.</summary>
public sealed class ImageDecodeException : Exception
{
    public ImageDecodeException(string message) : base(message)
    {
    }

    public ImageDecodeException(string message, Exception inner) : base(message, inner)
    {
    }
}
