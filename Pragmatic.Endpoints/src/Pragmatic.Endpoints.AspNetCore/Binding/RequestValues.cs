using Microsoft.AspNetCore.Http;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     Reads raw request values by name. The half of binding that has nothing to do with types.
/// </summary>
/// <remarks>
///     Exists because generated endpoints cannot let ASP.NET bind their parameters. The minimal-API
///     binder runs through <c>RequestDelegateFactory</c>, which is reflection-based; ASP.NET's answer
///     for AOT is the Request Delegate Generator, and one source generator cannot see another's
///     output. Measured: an endpoint mapped with a handler <c>Delegate</c> and published Native AOT
///     returns 500, or 200 with an empty body when the reflection fallback is left on.
/// </remarks>
public static class RequestValues
{
    /// <summary>The route value with this name, or <c>null</c> when the route did not supply it.</summary>
    public static string? Route(HttpContext context, string name)
        => context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() : null;

    /// <summary>The first query value with this name, or <c>null</c>.</summary>
    public static string? Query(HttpContext context, string name)
        => context.Request.Query.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>Every query value with this name, in order. Empty when absent.</summary>
    public static string?[] QueryAll(HttpContext context, string name)
        => context.Request.Query.TryGetValue(name, out var values) ? [.. values] : [];

    /// <summary>The first header value with this name, or <c>null</c>.</summary>
    public static string? Header(HttpContext context, string name)
        => context.Request.Headers.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>The cookie with this name, or <c>null</c>.</summary>
    public static string? Cookie(HttpContext context, string name)
        => context.Request.Cookies.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    ///     The uploaded file with this name, falling back to the first file in the form.
    /// </summary>
    /// <remarks>
    ///     The fallback is what ASP.NET's own binder does for a single <c>IFormFile</c> parameter, and
    ///     dropping it would break every client that posts one file without naming the field.
    /// </remarks>
    public static IFormFile? File(IFormCollection form, string? name)
        => (name is null ? null : form.Files.GetFile(name)) ?? (form.Files.Count > 0 ? form.Files[0] : null);

    /// <summary>The form field with this name, or <c>null</c>.</summary>
    /// <remarks>
    ///     Takes the already-read <see cref="IFormCollection" /> rather than the context: reading the
    ///     form is what throws when antiforgery has failed, so the caller does it once, after the
    ///     check, and every field comes from the same read.
    /// </remarks>
    public static string? Form(IFormCollection form, string name)
        => form.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;
}
