// =============================================================================
// Sample error type for demonstrations
// =============================================================================

namespace Pragmatic.Result.Samples.Errors;

public sealed record ValidationError(string Message) : Error
{
    public override string Code => "VALIDATION";
    public override int StatusCode => 400;
}