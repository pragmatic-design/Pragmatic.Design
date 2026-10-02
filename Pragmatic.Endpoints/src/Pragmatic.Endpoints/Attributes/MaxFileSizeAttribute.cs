namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies the maximum allowed file size in bytes for an <c>IFormFile</c> property.
///     The source generator emits a check before <c>HandleAsync</c>, returning HTTP 413
///     (Payload Too Large) if the file exceeds this limit.
/// </summary>
/// <remarks>
///     This is an HTTP-boundary validation — it rejects oversized uploads early,
///     before the request reaches business logic.
/// </remarks>
/// <example>
///     <code>
/// [FromForm]
/// [MaxFileSize(10 * 1024 * 1024)] // 10 MB
/// public IFormFile Photo { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxFileSizeAttribute(long maxBytes) : Attribute
{
    /// <summary>
    ///     The maximum file size in bytes.
    /// </summary>
    public long MaxBytes { get; } = maxBytes;
}
