namespace Showcase.Booking.Dtos;

/// <summary>
/// Result DTO for room availability search.
/// </summary>
public sealed record AvailableRoomResult
{
    public Guid RoomTypeId { get; init; }
    public string RoomTypeName { get; init; } = "";
    public int MaxOccupancy { get; init; }
    public decimal BaseRate { get; init; }
    public string Currency { get; init; } = "EUR";
    public int AvailableRooms { get; init; }
}
