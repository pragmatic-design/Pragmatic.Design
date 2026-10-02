namespace Showcase.Booking.Errors;

public sealed record ValidationError : Error
{
    public override string Code => "VALIDATION_ERROR";
    public override int StatusCode => 400;
    public override string Title => "Validation Failed";

    public required string Field { get; init; }
    public required string Message { get; init; }
}
