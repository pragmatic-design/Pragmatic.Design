using Pragmatic.Mapping.Attributes;
using Conformance.Poco.Shapes;

namespace Conformance.Poco.Dtos;

/// <summary>The single navigation, for the one-to-one merge case.</summary>
[MapFrom<BasketOwner>]
[MapTo<BasketOwner>]
public partial record BasketOwnerDto
{
    public int Id { get; init; }

    public string Name { get; init; } = "";
}
