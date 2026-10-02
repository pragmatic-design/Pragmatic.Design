namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Limits the total request body size for this endpoint; oversized requests are rejected
///     with 413 Payload Too Large before the body is read.
///     Distinct from <see cref="MaxFileSizeAttribute" />, which validates individual uploaded
///     files after multipart parsing.
/// </summary>
/// <param name="bytes">Maximum allowed request body size in bytes (must be positive).</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MaxBodySizeAttribute(long bytes) : Attribute
{
    /// <summary>Maximum allowed request body size in bytes.</summary>
    public long Bytes { get; } = bytes;
}
