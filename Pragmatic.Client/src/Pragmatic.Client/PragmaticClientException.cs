namespace Pragmatic.Client;

/// <summary>
///     Exception thrown when an API client receives an unexpected response
///     that cannot be mapped to a typed error.
/// </summary>
public sealed class PragmaticClientException : Exception
{
    /// <summary>Initializes with a message and HTTP status code.</summary>
    public PragmaticClientException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    ///     Initializes with a message, HTTP status code, and the original transport exception.
    ///     Use this overload when wrapping lower-level exceptions (e.g., <see cref="HttpRequestException"/>)
    ///     so the original cause is preserved in <see cref="Exception.InnerException"/>.
    /// </summary>
    public PragmaticClientException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code from the response, or 0 if the request never completed.</summary>
    public int StatusCode { get; }
}
