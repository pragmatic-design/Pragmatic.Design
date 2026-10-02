using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     E2E for the Pragmatic.Temporal module: business-day arithmetic via ITemporalCalculator,
///     LocalDate serialized on the wire (ISO yyyy-MM-dd by Pragmatic.Temporal.Json), and the
///     host-generated AddPragmaticTemporalAspNetCore wiring (Showcase.Host references
///     Pragmatic.Temporal.AspNetCore, so the SG emits the web integration).
/// </summary>
public class TemporalCheckoutFollowUpTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CheckoutFollowUp_OnFriday_FollowUpIsMonday()
    {
        // 2027-01-15 is a Friday → next business day is Monday 2027-01-18.
        var response = await GetRawAsync("/api/reservations/checkout-followup?checkOut=2027-01-15");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("checkOut").GetString().Should().Be("2027-01-15",
            "LocalDate must serialize as ISO yyyy-MM-dd on the wire");
        json.GetProperty("isBusinessDay").GetBoolean().Should().BeTrue();
        json.GetProperty("billingFollowUpDate").GetString().Should().Be("2027-01-18",
            "the business day after a Friday checkout is the following Monday");
    }

    [Fact]
    public async Task CheckoutFollowUp_OnSaturday_IsNotBusinessDay()
    {
        // 2027-01-16 is a Saturday.
        var response = await GetRawAsync("/api/reservations/checkout-followup?checkOut=2027-01-16");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("isBusinessDay").GetBoolean().Should().BeFalse();
        json.GetProperty("billingFollowUpDate").GetString().Should().Be("2027-01-18");
    }

    [Fact]
    public async Task CheckoutFollowUp_CountsBusinessAndCalendarDays()
    {
        var response = await GetRawAsync("/api/reservations/checkout-followup?checkOut=2027-06-30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var businessDays = json.GetProperty("businessDaysUntilCheckout").GetInt32();
        var calendarDays = json.GetProperty("calendarDaysUntilCheckout").GetInt32();

        businessDays.Should().BeGreaterThan(0);
        calendarDays.Should().BeGreaterThanOrEqualTo(businessDays,
            "calendar days always include the business days of the range");
    }
}
