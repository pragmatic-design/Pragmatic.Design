using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Endpoints.Context;

/// <summary>
///     Provides context information for endpoint processing.
/// </summary>
/// <remarks>
///     <para>
///         The context is available to pre/post processors and provides
///         access to the HTTP context, user, and endpoint metadata.
///     </para>
/// </remarks>
public sealed class EndpointContext : IEndpointContext
{
    private readonly Dictionary<string, object?> _items;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EndpointContext" /> class.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <param name="endpointName">The endpoint name.</param>
    /// <param name="endpoint">The endpoint instance.</param>
    public EndpointContext(HttpContext httpContext, string endpointName, object endpoint)
    {
        HttpContext = httpContext;
        _items = new Dictionary<string, object?>();
        EndpointName = endpointName;
        Endpoint = endpoint;
    }

    /// <summary>
    ///     Gets the endpoint name.
    /// </summary>
    public string EndpointName { get; }

    /// <summary>
    ///     Gets the endpoint instance.
    /// </summary>
    public object Endpoint { get; }

    /// <summary>
    ///     Gets the current user.
    /// </summary>
    public ClaimsPrincipal? User => HttpContext.User;

    /// <summary>
    ///     Gets the HTTP request.
    /// </summary>
    public HttpRequest Request => HttpContext.Request;

    /// <summary>
    ///     Gets the HTTP response.
    /// </summary>
    public HttpResponse Response => HttpContext.Response;

    /// <summary>
    ///     Gets the cancellation token.
    /// </summary>
    public CancellationToken CancellationToken => HttpContext.RequestAborted;

    /// <summary>
    ///     Gets the underlying HTTP context.
    /// </summary>
    public HttpContext HttpContext { get; }

    /// <summary>
    ///     Gets or sets a value in the context items dictionary.
    /// </summary>
    /// <param name="key">The key.</param>
    public object? this[string key]
    {
        get => _items.TryGetValue(key, out var value) ? value : null;
        set => _items[key] = value;
    }

    /// <summary>
    ///     Gets a route value as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to convert to.</typeparam>
    /// <param name="name">The route parameter name.</param>
    /// <returns>The converted value or default.</returns>
    public T? GetRouteValue<T>(string name)
    {
        var value = HttpContext.Request.RouteValues[name];
        if (value is null)
            return default;

        if (value is T typed)
            return typed;

        return TryConvert<T>(value?.ToString());
    }

    /// <summary>
    ///     Gets a query string value as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to convert to.</typeparam>
    /// <param name="name">The query parameter name.</param>
    /// <returns>The converted value or default.</returns>
    public T? GetQueryValue<T>(string name)
    {
        var value = HttpContext.Request.Query[name].FirstOrDefault();
        if (string.IsNullOrEmpty(value))
            return default;

        return TryConvert<T>(value);
    }

    private static T? TryConvert<T>(string? value)
    {
        if (value is null) return default;
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        try
        {
            if (targetType == typeof(Guid))
                return Guid.TryParse(value, out var g) ? (T)(object)g : default;

            if (targetType.IsEnum)
                return Enum.TryParse(targetType, value, ignoreCase: true, out var e) ? (T)e! : default;

            return (T)Convert.ChangeType(value, targetType);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    ///     Gets a header value.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>The header value or null.</returns>
    public string? GetHeader(string name)
    {
        return HttpContext.Request.Headers[name].FirstOrDefault();
    }

    /// <summary>
    ///     Sets a response header.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    public void SetResponseHeader(string name, string value)
    {
        HttpContext.Response.Headers[name] = value;
    }
}