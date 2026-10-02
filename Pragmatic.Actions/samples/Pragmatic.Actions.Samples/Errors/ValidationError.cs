using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Errors;

/// <summary>
///     Error returned when input validation fails.
/// </summary>
public sealed record ValidationError : Error
{
    /// <inheritdoc />
    public override string Code => "VALIDATION_ERROR";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Validation Failed";

    /// <summary>
    ///     The field that failed validation.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    ///     Description of the validation failure.
    /// </summary>
    public required string Message { get; init; }
}
