namespace Showcase.Booking.Dtos;

/// <summary>
/// A staff assignment as a client sees it.
/// Demonstrates: MapFrom on a temporal relation — the validity period is part of the shape.
/// </summary>
[MapFrom<StaffAssignment>]
[GenerateProjection]
public partial class StaffAssignmentDto
{
    public Guid Id { get; init; }
    public Guid StaffId { get; init; }
    public Guid PropertyId { get; init; }
    public string Role { get; init; } = "";
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
}
