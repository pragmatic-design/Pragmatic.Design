namespace Showcase.Catalog.RoomTypes.Mutations;

/// <summary>
/// Creates a new room type under a property.
/// Demonstrates: Mutation&lt;T&gt; + [Mutation] + [Endpoint] with FK property (PropertyId).
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/room-types")]
[RequirePermission(CatalogPermissions.RoomType.Create)]
[ReturnsDto<RoomTypeCreatedDto>]
public partial class CreateRoomTypeMutation : Mutation<RoomType>
{
    public required Guid PropertyId { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public int? MaxOccupancy { get; init; }
    public required decimal BaseRate { get; init; }
    public string? Currency { get; init; }
    public int? TotalRooms { get; init; }
}
