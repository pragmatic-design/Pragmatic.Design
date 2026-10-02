using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Types;

namespace Showcase.Booking.Endpoints;

/// <summary>
/// Plans the billing follow-up for a checkout date using business-day arithmetic.
/// Demonstrates: Pragmatic.Temporal end-to-end — ITemporalCalculator (business days,
/// country holidays), LocalDate/DateRange types, IClock for "today", and LocalDate
/// serialized on the wire by Pragmatic.Temporal.Json.
/// </summary>
[Endpoint(HttpVerb.Get, "/api/reservations/checkout-followup")]
[ApiSummary("Plan Checkout Follow-Up")]
[ApiTags("Reservations")]
[AllowAnonymous]
public partial class PlanCheckoutFollowUpEndpoint : Endpoint<CheckoutFollowUpResult>
{
    private ITemporalCalculator _calculator = null!;
    private IClock _clock = null!;

    /// <summary>Checkout date (ISO yyyy-MM-dd).</summary>
    [FromQuery]
    public DateOnly CheckOut { get; set; }

    /// <summary>Optional ISO country code for public-holiday awareness (e.g. "IT").</summary>
    [FromQuery]
    public string? Country { get; set; }

    public override Task<Result<CheckoutFollowUpResult>> HandleAsync(CancellationToken ct = default)
    {
        LocalDate checkOut = CheckOut;
        LocalDate today = _clock.Today;

        var followUp = Country is { Length: > 0 }
            ? _calculator.NextBusinessDay(checkOut, Country)
            : _calculator.NextBusinessDay(checkOut);

        var isBusinessDay = Country is { Length: > 0 }
            ? _calculator.IsBusinessDay(checkOut, Country)
            : _calculator.IsBusinessDay(checkOut);

        var businessDays = Country is { Length: > 0 }
            ? _calculator.CountBusinessDays(today, checkOut, Country)
            : _calculator.CountBusinessDays(today, checkOut);

        var window = today <= checkOut ? DateRange.Between(today, checkOut) : DateRange.SingleDay(checkOut);

        var result = new CheckoutFollowUpResult
        {
            CheckOut = checkOut,
            IsBusinessDay = isBusinessDay,
            BillingFollowUpDate = followUp,
            BusinessDaysUntilCheckout = businessDays,
            CalendarDaysUntilCheckout = window.Days
        };

        return Task.FromResult(Result<CheckoutFollowUpResult>.Success(result));
    }
}
