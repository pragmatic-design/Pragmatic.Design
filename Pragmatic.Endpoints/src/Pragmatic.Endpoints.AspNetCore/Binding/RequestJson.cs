using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     The <see cref="JsonTypeInfo{T}" /> a generated endpoint uses to read its request body.
/// </summary>
/// <remarks>
///     Taken from the request's configured <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions" />, so
///     it goes through the same resolver chain as every other response and honours
///     <c>DisableReflectionFallback()</c>. Reading the body with the untyped overload instead would put
///     reflection back on the one path AOT cannot afford.
/// </remarks>
public static class RequestJson
{
    /// <summary>The metadata for <typeparamref name="T" /> under this request's serializer options.</summary>
    /// <exception cref="InvalidOperationException">
    ///     The resolver has no metadata for the type — a generated context that does not cover it. Loud
    ///     on purpose: the alternative is a body that deserializes to nothing and an action that runs on
    ///     defaults.
    /// </exception>
    public static JsonTypeInfo<T> TypeInfo<T>(HttpContext context)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        // TryGetTypeInfo, so the message below is the one the caller sees: GetTypeInfo throws its own
        // NotSupportedException first, which names the resolver rather than what to do about it.
        return options.TryGetTypeInfo(typeof(T), out var info) && info is JsonTypeInfo<T> typed
            ? typed
            : throw new InvalidOperationException(
                $"No JSON metadata for '{typeof(T)}'. The generated JsonSerializerContext does not cover it.");
    }
}
