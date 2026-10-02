namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies where the response can be cached.
/// </summary>
public enum ResponseCacheLocation
{
    /// <summary>
    ///     Response can be cached by any cache (client, proxy, server).
    /// </summary>
    Any,

    /// <summary>
    ///     Response can only be cached by the client.
    /// </summary>
    Client,

    /// <summary>
    ///     Response should not be cached.
    /// </summary>
    None
}
