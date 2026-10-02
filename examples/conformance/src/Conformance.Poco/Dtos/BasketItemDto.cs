using Pragmatic.Mapping.Attributes;
using Conformance.Poco.Shapes;

namespace Conformance.Poco.Dtos;

/// <summary>
///     The first-level child, which carries the second.
/// </summary>
/// <remarks>
///     It publishes <c>Tags</c> in both navigation lists: its two shapes coincide, so
///     <c>RequiredNavigations</c> and <c>WrittenNavigations</c> are equal. The case in which they
///     diverge needs a purpose-built DTO.
/// </remarks>
[MapFrom<BasketItem>]
[MapTo<BasketItem>]
public partial record BasketItemDto
{
    public int Id { get; init; }

    public string Product { get; init; } = "";

    public int Quantity { get; init; }

    public List<ItemTagDto> Tags { get; init; } = [];
}
