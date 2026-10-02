namespace Pragmatic.Http;

/// <summary>
///     Endpoint metadata carrying the per-endpoint request body size limit in bytes.
///     Emitted by the source generator from <c>[MaxBodySize]</c> and applied by the host
///     request-limits pipeline step before the body is read.
/// </summary>
/// <param name="MaxBytes">Maximum allowed request body size in bytes.</param>
public sealed record MaxBodySizeMetadata(long MaxBytes);
