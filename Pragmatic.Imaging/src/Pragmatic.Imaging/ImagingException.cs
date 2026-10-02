namespace Pragmatic.Imaging;

/// <summary>
/// Exception thrown when a native imaging operation fails or a safety limit is violated.
/// Inspect <see cref="Reason"/> to branch on the failure kind.
/// </summary>
public sealed class ImagingException : Exception
{
    public ImagingException(string message, ImagingError reason = ImagingError.NativeError)
        : base(message) => Reason = reason;

    public ImagingException(string message, Exception innerException, ImagingError reason = ImagingError.NativeError)
        : base(message, innerException) => Reason = reason;

    /// <summary>The category of failure that produced this exception.</summary>
    public ImagingError Reason { get; }
}
