namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     A logged type with one member of every kind a generated writer handles, a collection of children that
///     declare personal data, and a collection declared not logged as a whole.
/// </summary>
public sealed record Shipment(
    Guid Id,
    DateTimeOffset DispatchedAt,
    DateTime Due,
    TimeSpan Window,
    char Grade,
    decimal Weight,
    double Ratio,
    int? Parcels,
    bool Fragile,
    List<ShipmentLine> Lines,
    string[] Labels,
    [property: NotLogged] string[] Codes);
