using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Generated;
using Xunit;

namespace Showcase.Tests.Unit;

/// <summary>
///     What the generated AsyncAPI document says about which Booking events cross the boundary.
/// </summary>
/// <remarks>
///     <para>
///         The document is a compile-time constant in the assembly that owns the events, and it is
///         the only place a consumer reads the shape and the channel of one. Every Booking event was
///         catalogued <c>x-pragmatic-public: false</c> — including the three
///         <c>Showcase.Billing</c> handles, which is the whole reason they carry their data instead
///         of a reference. The published contract contradicted the architecture, and nothing said so.
///     </para>
///     <para>
///         ⚠️ <c>[PublicEvent]</c> changes no behaviour: the events were already raised, already
///         handled, already on the same channel. It changes what the contract states, which is the
///         only thing a consumer of that contract has to go on.
///     </para>
/// </remarks>
public class WhichEventsTheContractCallsPublic
{
    private static JsonElement Message(string simpleName)
        => JsonDocument.Parse(PragmaticAsyncApi.Json).RootElement.Clone()
            .GetProperty("components").GetProperty("messages")
            .GetProperty($"Showcase.Booking.Events.{simpleName}");

    [Theory]
    [InlineData(nameof(Showcase.Booking.Events.ReservationCreated))]
    [InlineData(nameof(Showcase.Booking.Events.ReservationConfirmed))]
    [InlineData(nameof(Showcase.Booking.Events.ReservationCancelled))]
    public void AnEventBillingHandles_IsPublishedAsPartOfTheContract(string simpleName)
    {
        Message(simpleName).GetProperty("x-pragmatic-public").GetBoolean().Should().BeTrue(
            "Showcase.Billing has a handler for it, so it is part of the cross-boundary contract "
            + "and a consumer reading this document has to be told so");
    }

    /// <summary>
    ///     The control: an event nobody outside Booking handles stays internal.
    /// </summary>
    /// <remarks>
    ///     Without it, "the three are public" is satisfied by a generator that marks everything — and
    ///     a contract in which every event is public says nothing about any of them.
    ///     <c>GuestCheckedIn</c> is handled by <c>GuestCheckedInHandler</c>, inside Booking, and by
    ///     nobody else.
    /// </remarks>
    [Fact]
    public void AnEventOnlyBookingHandles_StaysInternal()
    {
        Message(nameof(Showcase.Booking.Events.GuestCheckedIn))
            .GetProperty("x-pragmatic-public").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    ///     And the document still describes the payload, so "public" is not the only thing in it.
    /// </summary>
    [Fact]
    public void ThePublicEvent_StillCarriesItsPayload()
    {
        var properties = Message(nameof(Showcase.Booking.Events.ReservationConfirmed))
            .GetProperty("payload").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ToList();

        properties.Should().Contain("reservationId", "the reservation the event is about");
        properties.Should().Contain("totalAmount", "and what Billing raises the invoice for");
    }
}
