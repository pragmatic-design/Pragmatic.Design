using Pragmatic.Privacy;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>A line of a <see cref="Shipment" />, with a member that is personal data.</summary>
public sealed record ShipmentLine(string Sku, [property: PersonalData(DataCategory.Contact)] string Recipient);
