using System.Security.Claims;

namespace Pragmatic.Endpoints.Context;

/// <summary>
///     Abstraction for endpoint context, providing access to request metadata
///     without requiring ASP.NET Core dependencies.
/// </summary>
public interface IEndpointContext
{
    /// <summary>
    ///     Gets the endpoint name.
    /// </summary>
    string EndpointName { get; }

    /// <summary>
    ///     Gets the endpoint instance.
    /// </summary>
    object Endpoint { get; }

    /// <summary>
    ///     Gets the current user.
    /// </summary>
    ClaimsPrincipal? User { get; }

    /// <summary>
    ///     Gets the cancellation token.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    ///     Gets or sets a value in the context items dictionary.
    /// </summary>
    /// <param name="key">The key.</param>
    object? this[string key] { get; set; }

    /// <summary>
    ///     Gets a route value as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to convert to.</typeparam>
    /// <param name="name">The route parameter name.</param>
    /// <returns>The converted value or default.</returns>
    T? GetRouteValue<T>(string name);

    /// <summary>
    ///     Gets a query string value as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to convert to.</typeparam>
    /// <param name="name">The query parameter name.</param>
    /// <returns>The converted value or default.</returns>
    T? GetQueryValue<T>(string name);

    /// <summary>
    ///     Gets a header value.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>The header value or null.</returns>
    string? GetHeader(string name);

    /// <summary>
    ///     Sets a response header.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    void SetResponseHeader(string name, string value);
}
