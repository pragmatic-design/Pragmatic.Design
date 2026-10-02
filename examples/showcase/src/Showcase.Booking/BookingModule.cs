using Showcase.Catalog;

namespace Showcase.Booking;

/// <summary>
/// Reservation and guest domain module — depends on Catalog for property/room lookups.
/// </summary>
[Module(Name = "Showcase.Booking", Version = "1.0.0", Description = "Reservation and guest management")]
[IncludeModule<CatalogModule>]
public sealed class BookingModule;
