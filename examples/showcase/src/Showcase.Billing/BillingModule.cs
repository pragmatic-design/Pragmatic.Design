using Showcase.Booking;

namespace Showcase.Billing;

/// <summary>
/// Invoice and payment domain module — depends on Booking for reservation data.
/// </summary>
[Module(Name = "Showcase.Billing", Version = "1.0.0", Description = "Invoice and payment processing")]
[IncludeModule<BookingModule>]
public sealed class BillingModule;
