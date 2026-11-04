using Pragmatic.Mapping.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>
///     A reservation as a card, mapped in memory (#505): its property's name read through the navigation, and a card assembled after the row
///     arrives, which no engine translates, as the Showcase's <c>ReservationCardDto</c>.
/// </summary>
[MapFrom<Reservation>]
public partial class ReservationCard
{
    public string OwnerId { get; init; } = "";

    [MapProperty("Property.Name")]
    public string PropertyName { get; init; } = "";

    [MapIgnore]
    public string Card => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{OwnerId}@{PropertyName}");
}
