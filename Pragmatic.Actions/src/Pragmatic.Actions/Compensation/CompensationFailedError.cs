using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     An action failed, and undoing what an inner boundary had already committed failed too.
/// </summary>
/// <remarks>
///     Reported instead of the original failure on purpose. The two say different things: the original
///     error means "your operation did not happen", this one means "your operation did not happen and
///     the system is now inconsistent". Collapsing the second into the first hides the only fact an
///     operator can act on, and the caller is the only party still holding the request.
/// </remarks>
public sealed record CompensationFailedError : Error
{
    /// <inheritdoc />
    public override string Code => "COMPENSATION_FAILED";

    /// <inheritdoc />
    public override int StatusCode => 500;

    /// <inheritdoc />
    public override string Title => "The operation failed and could not be fully undone";

    /// <summary>Why the operation failed in the first place.</summary>
    public required IError OriginalError { get; init; }

    /// <summary>Why the undo failed, leaving committed work behind.</summary>
    public required IError CompensationError { get; init; }

    /// <summary>The action whose committed work could not be undone.</summary>
    public required string UncompensatedAction { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        extensions["originalError"] = OriginalError.Code;
        extensions["compensationError"] = CompensationError.Code;
        extensions["uncompensatedAction"] = UncompensatedAction;
    }
}
