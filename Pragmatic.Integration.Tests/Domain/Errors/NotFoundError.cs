using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Errors;

/// <summary>
///     Error returned when a requested resource is not found.
/// </summary>
public sealed record NotFoundError : Error
{
    /// <inheritdoc />
    public override string Code => "NOT_FOUND";

    /// <inheritdoc />
    public override int StatusCode => 404;

    /// <inheritdoc />
    public override string Title => "Resource Not Found";

    /// <summary>
    ///     The type of resource that was not found.
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    ///     The identifier of the resource that was not found.
    /// </summary>
    public required string ResourceId { get; init; }
}
