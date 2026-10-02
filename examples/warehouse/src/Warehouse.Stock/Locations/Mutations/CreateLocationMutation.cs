namespace Warehouse.Stock.Locations.Mutations;

/// <summary>A new place to keep stock.</summary>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(StockPermissions.Location.Create)]
[Endpoint(HttpVerb.Post, "api/locations")]
[CreatedAt("/api/locations/{Id}")]
[ReturnsDto<LocationDto>]
public partial class CreateLocationMutation : Mutation<Location>
{
    [Required]
    [MaxLength(20)]
    public required string Code { get; init; }

    [MaxLength(120)]
    public string? Description { get; init; }
}
