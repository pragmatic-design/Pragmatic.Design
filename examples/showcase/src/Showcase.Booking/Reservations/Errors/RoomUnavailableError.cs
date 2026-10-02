namespace Showcase.Booking.Errors;

/// <summary>
/// Room is not available for the requested dates.
/// Demonstrates: custom Error record with typed context properties. Declared <c>partial</c> so the
/// source generator emits the <c>WriteExtensions</c> override — the four context properties then
/// surface as camelCase ProblemDetails extensions (propertyId/roomTypeId/checkIn/checkOut) on the wire.
/// Properties are <c>init</c> (not <c>required</c>): a partial type with <c>required</c> members is also
/// picked up by the Validation generator as an implicit-[Required] validatable, which is meaningless for
/// an error carrier. The action always populates all four via the object initializer below.
/// </summary>
public sealed partial record RoomUnavailableError : Error
{
    public override string Code => "ROOM_UNAVAILABLE";
    public override int StatusCode => 409;
    public override string Title => "Room Unavailable";

    public Guid PropertyId { get; init; }
    public Guid RoomTypeId { get; init; }
    public DateTimeOffset CheckIn { get; init; }
    public DateTimeOffset CheckOut { get; init; }
}
