using Pragmatic.Result;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Error returned when a concurrency conflict is detected during save.
/// </summary>
/// <remarks>
///     <para>
///         This occurs when another user/process has modified the same entity
///         between the time it was loaded and when the save was attempted.
///     </para>
///     <para>
///         The caller should reload the entity, merge changes, and retry.
///     </para>
/// </remarks>
public sealed record ConcurrencyError : Error
{
    /// <inheritdoc />
    public override string Code => "CONCURRENCY_CONFLICT";

    /// <inheritdoc />
    public override int StatusCode => 409;

    /// <summary>
    ///     A human-readable message describing the conflict.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    ///     The type name of the entity that caused the conflict, if known.
    /// </summary>
    public string? EntityTypeName { get; init; }
}
