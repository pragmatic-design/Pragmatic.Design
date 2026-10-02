using Pragmatic.Mapping.Attributes;
using Conformance.Poco.Shapes;

namespace Conformance.Poco.Dtos;

/// <summary>The leaf. No navigation, so both lists are empty.</summary>
[MapFrom<ItemTag>]
[MapTo<ItemTag>]
public partial record ItemTagDto
{
    public int Id { get; init; }

    public string Name { get; init; } = "";
}
