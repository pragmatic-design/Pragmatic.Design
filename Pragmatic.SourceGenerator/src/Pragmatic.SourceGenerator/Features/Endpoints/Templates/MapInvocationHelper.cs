namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Renders the minimal-API map invocation prefix for an HTTP verb.
///     Minimal APIs have no MapHead/MapOptions extension methods, so those verbs
///     go through MapMethods with an explicit verb array.
/// </summary>
internal static class MapInvocationHelper
{
    /// <summary>
    ///     Returns the invocation up to (and excluding) the handler argument,
    ///     e.g. <c>MapGet("/route", </c> or <c>MapMethods("/route", new[] { "HEAD" }, </c>.
    ///     <paramref name="escapedRoute" /> must already be a valid C# string-literal body.
    /// </summary>
    public static string Render(string httpMethod, string escapedRoute)
        => httpMethod is "Head" or "Options"
            ? $"MapMethods(\"{escapedRoute}\", new[] {{ \"{httpMethod.ToUpperInvariant()}\" }}, "
            : $"Map{httpMethod}(\"{escapedRoute}\", ";
}
