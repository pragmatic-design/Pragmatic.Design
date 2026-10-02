using System.Text.Json;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     A channel's <c>address</c> is where the transport actually carries the event, so a
///     consumer that reads the published contract subscribes to something.
/// </summary>
/// <remarks>
///     <para>
///         It was the event's fully qualified type name. Measured on a real contracts assembly:
///         <c>"address": "Casework.Intake.Events.VerificationRequested"</c>
///         while the broker carried the same event on <c>intake.events</c> —
///         <c>DefaultMessageRouter.GetTopic</c> derives the topic from the namespace's second segment.
///         The document whose whole job is to tell another team where to listen had, in the one field
///         that says where, the one value that is not the address.
///     </para>
///     <para>
///         ⚠️ <b>Two events of one boundary share a topic, and that is the case that matters.</b> The
///         channel <em>key</em> stays the fully qualified name — it is the document's identifier and has
///         to be unique — while the <c>address</c> is what a consumer configures, and there it is
///         legitimately the same string for both. A change that made the address unique per event would
///         look correct in a one-event document and be wrong in every real one.
///     </para>
///     <para>
///         ⚠️ <b>The rule is a copy, and the document says so.</b> The generator runs at compile time and
///         <c>IMessageRouter</c> is a runtime service, so the topic here is computed by mirroring
///         <c>DefaultMessageRouter</c>'s convention rather than by asking it. An application may register
///         its own router — and may register it in a different assembly from the one the events live in,
///         where this generator could not see it even in principle. So the document declares the rule it
///         assumed (<c>x-pragmatic-address-rule</c>) instead of guessing whether the assumption holds:
///         a reader can check it, and a wrong assumption is visible rather than silent.
///     </para>
/// </remarks>
public class TheChannelAddressIsTheTopicTheTransportUsesTests
{
    private static JsonElement DocumentOf(params AsyncApiEventModel[] events)
    {
        var source = new AsyncApiTemplate(events.ToEquatableArray(), "Casework.Intake.Contracts")
            .RenderOutput().Text;

        // The document is a C# verbatim string literal in the generated source: unescape the doubled
        // quotes and read the JSON, so the assertions are about the contract and not about its escaping.
        var start = source.IndexOf("@\"", StringComparison.Ordinal) + 2;
        var end = source.LastIndexOf("\";", StringComparison.Ordinal);
        var json = source.Substring(start, end - start).Replace("\"\"", "\"");

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static AsyncApiEventModel Event(string name, string ns) => new()
    {
        Name = name,
        Namespace = ns,
        IsPublic = true,
    };

    /// <summary>
    ///     The setpoint, in the shape the story asked for: two events of one boundary, two channels, one
    ///     address — the topic they share.
    /// </summary>
    [Fact]
    public void TwoEventsOfOneBoundary_HaveTwoChannels_AndTheOneAddressTheyShare()
    {
        var document = DocumentOf(
            Event("VerificationRequested", "Casework.Intake.Events"),
            Event("CaseDecided", "Casework.Intake.Events"));

        var channels = document.GetProperty("channels");

        channels.GetProperty("Casework.Intake.Events.VerificationRequested")
            .GetProperty("address").GetString().Should().Be("intake.events",
                "which is what DefaultMessageRouter.GetTopic answers for this namespace, and what the "
                + "consumer's queue is bound to");

        channels.GetProperty("Casework.Intake.Events.CaseDecided")
            .GetProperty("address").GetString().Should().Be("intake.events",
                "the second event of the same boundary travels on the same topic — the address is not "
                + "an identifier, the key is");
    }

    /// <summary>
    ///     Control — the keys stay the fully qualified names. A fix that made the address right by
    ///     collapsing the two channels into one would pass the assertion above and destroy the document.
    /// </summary>
    [Fact]
    public void TwoEventsOfOneBoundary_KeepTheirOwnChannelAndMessage()
    {
        var document = DocumentOf(
            Event("VerificationRequested", "Casework.Intake.Events"),
            Event("CaseDecided", "Casework.Intake.Events"));

        document.GetProperty("channels").EnumerateObject().Select(c => c.Name).Should().BeEquivalentTo(
            ["Casework.Intake.Events.CaseDecided", "Casework.Intake.Events.VerificationRequested"]);

        document.GetProperty("components").GetProperty("messages").EnumerateObject()
            .Select(m => m.Name).Count().Should().Be(2);
    }

    /// <summary>
    ///     Two boundaries, two topics: the address follows the namespace and is not one constant for the
    ///     whole document.
    /// </summary>
    [Fact]
    public void EventsOfTwoBoundaries_HaveTheirOwnAddresses()
    {
        var document = DocumentOf(
            Event("VerificationRequested", "Casework.Intake.Events"),
            Event("VerificationAnswered", "Casework.Verify.Events"));

        var channels = document.GetProperty("channels");

        channels.GetProperty("Casework.Intake.Events.VerificationRequested")
            .GetProperty("address").GetString().Should().Be("intake.events");
        channels.GetProperty("Casework.Verify.Events.VerificationAnswered")
            .GetProperty("address").GetString().Should().Be("verify.events");
    }

    /// <summary>
    ///     The convention is kebab-case on the boundary segment, so a two-word boundary is not run
    ///     together. Mirrors <c>DefaultMessageRouter.ToKebabCase</c>, which is the behaviour a broker
    ///     already has in production.
    /// </summary>
    [Fact]
    public void ATwoWordBoundary_IsKebabCased_LikeTheRouterDoes()
    {
        var document = DocumentOf(Event("LeaveRequested", "TimeOff.LeaveRequests.Events"));

        document.GetProperty("channels").GetProperty("TimeOff.LeaveRequests.Events.LeaveRequested")
            .GetProperty("address").GetString().Should().Be("leave-requests.events");
    }

    /// <summary>
    ///     A namespace with a single segment has no second one: the router falls back to the first, and
    ///     so does this. The case exists because a contracts assembly can be flat.
    /// </summary>
    [Fact]
    public void ASingleSegmentNamespace_FallsBackToThatSegment()
    {
        var document = DocumentOf(Event("SomethingHappened", "Contracts"));

        document.GetProperty("channels").GetProperty("Contracts.SomethingHappened")
            .GetProperty("address").GetString().Should().Be("contracts.events");
    }

    /// <summary>
    ///     An event in the global namespace answers <c>".events"</c>, and that is the router's own answer
    ///     for a type whose namespace is null — not a tidy empty string.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This case is here because the first version of the mirrored rule returned <c>""</c> for it,
    ///     and an existing template test caught the divergence. The odd-looking value is the honest one:
    ///     it is where such an event would really be published. A mirrored rule has to be tested against
    ///     what the original does, not against what it ought to do.
    /// </remarks>
    [Fact]
    public void AnEventInTheGlobalNamespace_AnswersWhatTheRouterAnswers()
    {
        var document = DocumentOf(new AsyncApiEventModel { Name = "SomethingHappened" });

        document.GetProperty("channels").GetProperty("SomethingHappened")
            .GetProperty("address").GetString().Should().Be(".events");
    }

    /// <summary>
    ///     The document declares which rule produced its addresses, because the generator cannot ask the
    ///     router: an application that registers its own is reading a document whose assumption is
    ///     written down.
    /// </summary>
    [Fact]
    public void TheDocument_DeclaresTheRuleItsAddressesCameFrom()
    {
        var document = DocumentOf(Event("VerificationRequested", "Casework.Intake.Events"));

        document.GetProperty("x-pragmatic-address-rule").GetString().Should().Contain("DefaultMessageRouter",
            "the rule is named so a reader can check it against the application's own IMessageRouter");
    }
}
