using Pragmatic.Mapping.Attributes;
using Conformance.Poco.Shapes;

namespace Conformance.Poco.Dtos;

/// <summary>
///     The root: a nested collection two deep, plus a single navigation.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates that the navigation lists are <b>deep and prefixed</b>:
///         <c>["Items", "Items.Tags", "Owner"]</c>.
///     </para>
///     <para>
///         And, by construction: this project does not reference EF Core, so <c>ApplyTo(entity)</c> is
///         generated even though the DTO writes navigations.
///     </para>
/// </remarks>
[MapFrom<Basket>]
[MapTo<Basket>]
public partial record BasketDto
{
    public int Id { get; init; }

    public string Label { get; init; } = "";

    public List<BasketItemDto> Items { get; init; } = [];

    public BasketOwnerDto? Owner { get; init; }
}
