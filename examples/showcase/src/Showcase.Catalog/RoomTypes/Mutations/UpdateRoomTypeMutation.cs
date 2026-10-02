namespace Showcase.Catalog.RoomTypes.Mutations;

/// <summary>
/// Updates a room type.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] — all nullable for partial update — and
/// the auto-include: <see cref="RoomTypeSummaryDto"/> flattens <c>Property.Name</c>, so the load
/// brings the navigation with it. Nothing here says <c>[EagerLoad("Property")]</c>; the generator
/// works it out from the shape the operation answers with.
/// </summary>
[Mutation(Mode = MutationMode.Update)]
[Endpoint(HttpVerb.Put, "api/room-types/{id}")]
[RequirePermission(CatalogPermissions.RoomType.Update)]
[ReturnsDto<RoomTypeSummaryDto>]
public partial class UpdateRoomTypeMutation : Mutation<RoomType>
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? MaxOccupancy { get; init; }
    public decimal? BaseRate { get; init; }
    public string? Currency { get; init; }
    public int? TotalRooms { get; init; }
}
