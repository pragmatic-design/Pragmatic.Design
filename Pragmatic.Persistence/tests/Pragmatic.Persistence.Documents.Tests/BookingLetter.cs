using Pragmatic.Mapping.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A booking as a letter, mapped in memory (#505): its guest's email read through the navigation, which the documents engine fills from the
///     guest's document, and a line assembled after the document arrives.
/// </summary>
[MapFrom<Booking>]
public partial class BookingLetter
{
    public int Nights { get; init; }

    [MapProperty("Guest.Email")]
    public string GuestEmail { get; init; } = "";

    [MapIgnore]
    public string Line => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{GuestEmail} x{Nights}");
}
