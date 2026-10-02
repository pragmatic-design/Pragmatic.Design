namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies which MIME content types are accepted for an <c>IFormFile</c> property.
///     The source generator emits a check before <c>HandleAsync</c>, returning HTTP 415
///     (Unsupported Media Type) if the file's content type is not in the allowed list.
/// </summary>
/// <remarks>
///     This is an HTTP-boundary validation — it rejects unsupported file types early,
///     before the request reaches business logic.
/// </remarks>
/// <example>
///     <code>
/// [FromForm]
/// [AllowedContentTypes("image/jpeg", "image/png", "image/webp")]
/// public IFormFile Photo { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AllowedContentTypesAttribute(params string[] contentTypes) : Attribute
{
    /// <summary>
    ///     The allowed MIME content types.
    /// </summary>
    public string[] ContentTypes { get; } = contentTypes;
}
