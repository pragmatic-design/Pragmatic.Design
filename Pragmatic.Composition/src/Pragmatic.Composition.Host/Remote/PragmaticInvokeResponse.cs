using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Wire format for the <c>/_pragmatic/invoke</c> endpoint response.
///     On success, <see cref="Value" /> contains the serialized result.
///     On failure, <see cref="Error" /> contains ProblemDetails.
/// </summary>
/// <remarks>
///     <para>
///         <b>Success/failure is determined by <see cref="IsSuccess"/>, NOT by <c>Value is null</c>.</b>
///         <see cref="Value"/> is a nullable <see cref="JsonElement"/>: when the field is absent from the
///         wire JSON, <see cref="System.Text.Json"/> may surface it as a non-null element with
///         <see cref="JsonElement.ValueKind"/> == <see cref="JsonValueKind.Undefined"/> rather than as a
///         C# <c>null</c>. Callers that need to distinguish "no value" must check
///         <c>Value?.ValueKind is null or JsonValueKind.Undefined or JsonValueKind.Null</c>, after first
///         branching on <see cref="IsSuccess"/>. A void/no-result success legitimately carries no Value.
///     </para>
/// </remarks>
public sealed record PragmaticInvokeResponse(bool IsSuccess, JsonElement? Value, ProblemDetails? Error)
{
    /// <summary>HTTP status code of the operation (200 for success, error-specific for failure).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Correlation ID for distributed tracing.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Server-side processing duration in milliseconds.</summary>
    public long? DurationMs { get; init; }
}
