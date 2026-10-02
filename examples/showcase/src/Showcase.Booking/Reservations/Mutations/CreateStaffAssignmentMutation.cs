namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Assigns a staff member to a property, effective now.
/// Demonstrates: [TemporalRelation&lt;Property&gt;(MaxActive = 1)] is enforced automatically by the
/// mutation pipeline — a second active assignment for the same property is rejected with a
/// 409 without the operation having to call ValidateTemporalConstraints itself.
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/staff-assignments")]
[ReturnsDto<StaffAssignmentDto>]
public partial class CreateStaffAssignmentMutation : Mutation<StaffAssignment>
{
    public required Guid StaffId { get; init; }
    public required Guid PropertyId { get; init; }
    public required string Role { get; init; }

    // StaffId/PropertyId/Role auto-map to the entity's generated setters; ValidFrom is a public
    // temporal column (no generated Set method), so set it here to "now".
    public override Task<Result<StaffAssignment, IError>> ApplyAsync(StaffAssignment entity, CancellationToken ct = default)
    {
        entity.ValidFrom = DateTimeOffset.UtcNow;
        return Task.FromResult<Result<StaffAssignment, IError>>(entity);
    }
}
