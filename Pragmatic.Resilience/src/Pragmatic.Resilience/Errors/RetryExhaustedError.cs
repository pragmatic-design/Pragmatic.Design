using System.Text.Json.Serialization;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Errors;

/// <summary>All retry attempts exhausted.</summary>
/// <remarks>
///     <see cref="LastException"/> is marked <see cref="JsonIgnoreAttribute"/> so it
///     never reaches API clients via the default JSON serializer; internal stack
///     traces are observability data, not part of the public error contract. Server-
///     side logging code can still access the property directly for diagnostics.
/// </remarks>
public sealed record RetryExhaustedError(
    string OperationName,
    int Attempts,
    [property: JsonIgnore] Exception? LastException) : Error
{
    public override string Code => "RETRY_EXHAUSTED";
    public override int StatusCode => 503;
    public override string Title { get; } = $"All {Attempts} retry attempts exhausted for '{OperationName}'";
}
