using Pragmatic.Mapping.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>A room type's code, nested in <see cref="PropertyRoomsDto" />.</summary>
[MapFrom<RoomType>]
public partial class RoomTypeCodeDto
{
    public string Code { get; set; } = "";
}
