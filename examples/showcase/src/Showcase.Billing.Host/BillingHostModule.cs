using Showcase.Billing;

namespace Showcase.Billing.Host;

/// <summary>
///     Standalone Billing host module for distributed deployment.
///     Owns only the Billing boundary with its own financial database.
/// </summary>
/// <remarks>
///     ⚠️ <b>Booking is reached over HTTP, and that is not decoration.</b> Billing's
///     <c>InvoicePaidHandler</c> injects <c>IBookingActions</c>, so this host could not start without
///     somewhere for that interface to come from — it answered "Unable to resolve service for type
///     'Showcase.Booking.IBookingActions'" the first time anything booted it. A module that
///     calls another is deployable apart only if the call has a route; declaring it here is what gives
///     it one, and it makes the demo distributed in both directions — this host reaches Booking, and
///     <c>Showcase.Host.Distributed</c> reaches Billing.
/// </remarks>
[Module]
[Include<BillingModule, BillingFinancialDatabase>]
[RemoteBoundary<global::Showcase.Booking.BookingModule>]
public sealed class BillingHostModule;
