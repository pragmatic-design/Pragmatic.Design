using Pragmatic.Temporal.Types;

namespace Showcase.Booking.Dtos;

/// <summary>
/// Result DTO for the checkout follow-up planner.
/// Demonstrates: LocalDate on the wire (serialized as ISO yyyy-MM-dd by Pragmatic.Temporal.Json).
/// </summary>
public sealed record CheckoutFollowUpResult
{
    /// <summary>The requested checkout date.</summary>
    public LocalDate CheckOut { get; init; }

    /// <summary>Whether checkout falls on a business day (weekends/holidays excluded).</summary>
    public bool IsBusinessDay { get; init; }

    /// <summary>First business day after checkout — when billing follow-up runs.</summary>
    public LocalDate BillingFollowUpDate { get; init; }

    /// <summary>Business days between today and checkout (checkout excluded).</summary>
    public int BusinessDaysUntilCheckout { get; init; }

    /// <summary>Total calendar days between today and checkout (inclusive range).</summary>
    public int CalendarDaysUntilCheckout { get; init; }
}
