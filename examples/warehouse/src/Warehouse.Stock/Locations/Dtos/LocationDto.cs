namespace Warehouse.Stock.Dtos;

[MapFrom<Location>]
[GenerateProjection]
public partial class LocationDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";

    public string? Description { get; init; }
}
