using Microsoft.AspNetCore.Http.Metadata;

namespace Pragmatic.Endpoints.Binding;

/// <summary>
///     Declares the request content types an endpoint accepts.
/// </summary>
/// <remarks>
///     ASP.NET's own implementation of <see cref="IAcceptsMetadata" /> is internal, and the
///     <c>Accepts&lt;T&gt;()</c> extension that would build it is defined on <c>RouteHandlerBuilder</c> —
///     which a generated endpoint does not have, because it is mapped as a <c>RequestDelegate</c> so
///     that it survives an AOT publish. Twelve lines is a smaller price than the alternative.
/// </remarks>
/// <param name="contentTypes">The accepted content types.</param>
/// <param name="requestType">The type the body deserializes to, if any.</param>
/// <param name="isOptional">Whether the body may be omitted.</param>
public sealed class PragmaticAcceptsMetadata(
    string[] contentTypes,
    Type? requestType = null,
    bool isOptional = false) : IAcceptsMetadata
{
    /// <inheritdoc />
    public IReadOnlyList<string> ContentTypes { get; } = contentTypes;

    /// <inheritdoc />
    public Type? RequestType { get; } = requestType;

    /// <inheritdoc />
    public bool IsOptional { get; } = isOptional;
}
