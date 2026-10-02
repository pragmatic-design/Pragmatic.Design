namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Recognizes <c>Pragmatic.Endpoints.Responses.FileResponse</c>, the success type that turns an
///     endpoint or a remote action into a byte stream instead of JSON.
/// </summary>
/// <remarks>
///     The match is on the full name, not on the <c>FileResponse</c> suffix: a consumer type merely
///     <i>named</i> FileResponse would otherwise be routed through <c>FileResponseExtensions.ToResult</c>
///     and the remote streaming invoker, neither of which can accept it — the generated code would not
///     compile.
/// </remarks>
internal static class FileResponseType
{
    /// <summary>The fully qualified name as emitted in generated code.</summary>
    public const string FullyQualifiedName = "global::Pragmatic.Endpoints.Responses.FileResponse";

    private const string MetadataName = "Pragmatic.Endpoints.Responses.FileResponse";

    /// <summary>
    ///     Whether <paramref name="typeName" /> denotes the framework's <c>FileResponse</c>, with or
    ///     without the <c>global::</c> prefix.
    /// </summary>
    public static bool Is(string? typeName)
        => typeName is FullyQualifiedName or MetadataName;
}
