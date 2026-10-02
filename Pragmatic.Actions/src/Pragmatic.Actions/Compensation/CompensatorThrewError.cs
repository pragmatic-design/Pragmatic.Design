using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     A compensator threw instead of returning a failure.
/// </summary>
/// <remarks>
///     Turned into an error rather than left to propagate: the exception would replace the failure that
///     started the compensation, and the caller would see the undo's stack trace instead of the reason
///     their operation did not happen.
/// </remarks>
public sealed record CompensatorThrewError : Error
{
    /// <inheritdoc />
    public override string Code => "COMPENSATOR_THREW";

    /// <inheritdoc />
    public override int StatusCode => 500;

    /// <inheritdoc />
    public override string Title => "A compensator threw";

    /// <summary>The action whose compensator threw.</summary>
    public required string ActionName { get; init; }

    /// <summary>The exception message. The exception itself is not carried: errors are cache-safe.</summary>
    public required string Message { get; init; }
}
