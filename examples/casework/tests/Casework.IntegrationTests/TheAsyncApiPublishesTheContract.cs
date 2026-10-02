using System.Text.Json;
using Casework.Intake.Contracts.Generated;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The published contract has a readable face: the generated AsyncAPI document names the
///     event and the channel it travels on.
/// </summary>
/// <remarks>
///     <para>
///         The document is a compile-time constant in the assembly that owns the events
///         (<c>{Assembly}.Generated.PragmaticAsyncApi.Json</c>), which for Casework is the contract
///         project — so the one artefact a consumer needs is generated from the one assembly a consumer
///         references.
///     </para>
///     <para>
///         ⚠️ The document exists because <c>VerificationRequested</c> is a domain event. As a plain
///         record marked <c>[PublicEvent]</c> there is <b>no document at all</b>: this assembly's
///         compilation emits only the redaction map, because <c>AsyncApiFeature</c> gates on
///         <c>IDomainEvent</c> before reading the attribute.
///     </para>
/// </remarks>
public sealed class TheAsyncApiPublishesTheContract
{
    private static JsonElement Document =>
        JsonDocument.Parse(PragmaticAsyncApi.Json).RootElement.Clone();

    [Fact]
    public void TheDocument_IsAnAsyncApiDocument()
    {
        Document.GetProperty("asyncapi").GetString().Should().StartWith("3.",
            "the generator writes AsyncAPI 3.0, and a consumer's tooling reads the version first");
    }

    [Fact]
    public void TheContract_HasItsChannelAndItsMessage()
    {
        var document = Document;

        document.GetProperty("channels").EnumerateObject().Select(channel => channel.Name)
            .Should().Contain(Contract, "the event Intake publishes is what this document exists to say");

        var message = document.GetProperty("components").GetProperty("messages").GetProperty(Contract);

        message.GetProperty("x-pragmatic-public").GetBoolean().Should().BeTrue(
            "the document is where IIntegrationEvent becomes visible to a consumer: without the marker "
            + "the event would be catalogued as an internal one");

        var properties = message.GetProperty("payload").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ToList();

        properties.Should().Contain("caseId", "a consumer needs the case the verification is about");
        properties.Should().Contain("kind", "and what is to be verified");
        properties.Should().Contain("eventId", "and the id it deduplicates a redelivery on");
    }

    /// <summary>
    ///     The channel's <b>address</b> is the topic the broker carries the event on, so a consumer can
    ///     configure what it reads here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not the event's type name: an address of
    ///         <c>"Casework.Intake.Events.VerificationRequested"</c> while the message travels on
    ///         <c>intake.events</c> would have a consumer that follows the document subscribe to nothing.
    ///         The test asserts the agreement.
    ///     </para>
    ///     <para>
    ///         The <b>key</b> stays the fully qualified name: it is the document's identifier and has to
    ///         be unique, while the address is shared by every event of this boundary — <c>CaseDecided</c>
    ///         travels on the same topic. Asserting the key and the address separately is what keeps a
    ///         future change from making one right by collapsing the other.
    ///     </para>
    ///     <para>
    ///         ⚠️ And the document says which rule produced it (<c>x-pragmatic-address-rule</c>), because
    ///         the generator writes it at compile time and <c>IMessageRouter</c> is a runtime service:
    ///         this application uses the default, and a reader can check that.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheChannelsAddress_IsTheTopicTheBrokerCarriesItOn()
    {
        var channel = Document.GetProperty("channels").GetProperty(Contract);

        channel.GetProperty("address").GetString().Should().Be("intake.events",
            "which is what DefaultMessageRouter.GetTopic answers for this event's namespace, and the "
            + "exchange the message is published to");

        Document.GetProperty("x-pragmatic-address-rule").GetString().Should().Contain("DefaultMessageRouter",
            "the assumption behind the address is named, not implied");
    }

    private const string Contract = "Casework.Intake.Events.VerificationRequested";
}
