using Pragmatic.Mapping.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>A property with its room types, as a response: what it maps from reaches the <c>RoomTypes</c> navigation.</summary>
[MapFrom<Property>]
public partial class PropertyRoomsDto
{
    public string Name { get; set; } = "";

    public List<RoomTypeCodeDto> RoomTypes { get; set; } = [];
}
