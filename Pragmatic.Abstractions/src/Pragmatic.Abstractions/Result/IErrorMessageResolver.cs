namespace Pragmatic.Result;

/// <summary>
///     Resolves localized error messages and titles from error codes, at the serialization boundary.
/// </summary>
/// <remarks>
///     <para>
///         Implement it and register the implementation in DI; whatever turns an <see cref="IError" />
///         into a wire response will consult it. The default behaviour, with none registered, is the
///         error's own <see cref="IError.Title" /> and <see cref="IError.Description" />.
///     </para>
///     <para>
///         It lives in Abstractions, next to the <see cref="IError" /> whose doc has always pointed at
///         it, and not in the ASP.NET Core package where it started. That placement was the reason it
///         went unused: <c>Pragmatic.Endpoints.AspNetCore</c> — which is how a Pragmatic application
///         actually returns errors — does not reference <c>Pragmatic.Result.AspNetCore</c>, so it
///         could not name this type, let alone call it. Registering a resolver localized nothing
///         (I18N-B-001). The contract has no ASP.NET Core in it and never needed to be there.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public class ResourceErrorMessageResolver(IStringLocalizer localizer) : IErrorMessageResolver
/// {
///     public string? Resolve(string code, object? context) => localizer[code]?.Value;
/// }
/// </code>
/// </example>
public interface IErrorMessageResolver
{
    /// <summary>
    ///     Resolves a localized detail message for the given error code.
    /// </summary>
    /// <param name="code">The error code (e.g., "NOT_FOUND").</param>
    /// <param name="context">Optional context object containing error details.</param>
    /// <returns>The localized message, or null to use the error's own description.</returns>
    string? Resolve(string code, object? context = null);

    /// <summary>
    ///     Resolves a localized title for the given error code.
    /// </summary>
    /// <param name="code">The error code (e.g., "NOT_FOUND").</param>
    /// <param name="context">Optional context object containing error details.</param>
    /// <returns>The localized title, or null to use the error's own title.</returns>
    string? ResolveTitle(string code, object? context = null) => null;

    /// <summary>
    ///     Resolves the message a key names, in the current culture, with its parameters interpolated:
    ///     what a single validation issue says about its field.
    /// </summary>
    /// <param name="messageKey">The key as the issue carries it (e.g., "validation.required").</param>
    /// <param name="parameters">The issue's parameters, for the placeholders of the message.</param>
    /// <returns>The message, or null when the key is unknown.</returns>
    /// <remarks>
    ///     An error is resolved by its code; an issue has no code of its own, only a key. Without this
    ///     member nothing turned a key into words, and a validation failure answered every field with a
    ///     key in every language.
    /// </remarks>
    string? ResolveKey(string messageKey, IReadOnlyDictionary<string, object>? parameters = null) => null;
}
