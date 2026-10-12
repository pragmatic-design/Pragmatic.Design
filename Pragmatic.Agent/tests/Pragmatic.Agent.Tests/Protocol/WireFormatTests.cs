using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Protocol;
using Pragmatic.Agent.Protocol.Payloads;
using Pragmatic.Agent.Protocol.Serialization;
using Xunit;

namespace Pragmatic.Agent.Tests.Protocol;

/// <summary>
///     Serialization round-trip coverage for <see cref="JsonWireFormat" />, the live wire format used by
///     <see cref="Pragmatic.Agent.Protocol.FrameCodec" /> on the socket and gossip. It writes the protocol's own
///     types through generated metadata (#142), so it is exercised with them: a populated payload, a null member,
///     an empty collection, and a type outside the protocol.
/// </summary>
/// <remarks>
///     A binary (MessagePack) format was prototyped but removed — the message model is JSON-native
///     (<c>AgentMessage.Payload</c> is a <c>JsonElement</c>), so a real binary format needs a
///     format-neutral envelope + self-describing frames. See
///     <c>docs/future/agent-transport-security-and-binary-wireformat.md</c>.
/// </remarks>
public class WireFormatTests
{
    /// <summary>Not a protocol type: the format has no metadata for it.</summary>
    public sealed class NotAProtocolType
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void JsonWireFormat_FormatName_IsJson() =>
        new JsonWireFormat().FormatName.Should().Be("json");

    [Fact]
    public void JsonWireFormat_PopulatedPayload_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();
        var original = new KvEntryPayload { Key = "config/x", Value = "42", Version = 7, Found = true };

        var decoded = format.Deserialize<KvEntryPayload>(format.Serialize(original));

        decoded.Should().NotBeNull();
        decoded!.Key.Should().Be("config/x");
        decoded.Value.Should().Be("42");
        decoded.Version.Should().Be(7L);
        decoded.Found.Should().BeTrue();
    }

    [Fact]
    public void JsonWireFormat_NullMember_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();

        var decoded = format.Deserialize<KvEntryPayload>(format.Serialize(new KvEntryPayload { Key = "x", Value = null }));

        decoded.Should().NotBeNull();
        decoded!.Value.Should().BeNull();
    }

    [Fact]
    public void JsonWireFormat_EmptyCollection_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();

        var decoded = format.Deserialize<KvEntriesPayload>(format.Serialize(new KvEntriesPayload { Entries = [] }));

        decoded.Should().NotBeNull();
        decoded!.Entries.Should().BeEmpty();
    }

    [Fact]
    public void JsonWireFormat_Frame_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();
        var frame = new AgentMessage
        {
            Type = MessageType.Response,
            Id = "c-1",
            Success = true,
            Payload = AgentPayloads.ToElement(new KvSetResultPayload { Version = 3 }),
        };

        var decoded = format.Deserialize<AgentMessage>(format.Serialize(frame));

        decoded.Should().NotBeNull();
        decoded!.Id.Should().Be("c-1");
        AgentPayloads.From<KvSetResultPayload>(decoded.Payload!.Value)!.Version.Should().Be(3L);
    }

    [Fact]
    public void JsonWireFormat_PrimitiveString_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();

        format.Deserialize<string>(format.Serialize("hello")).Should().Be("hello");
    }

    /// <summary>
    ///     The boundary of generated metadata: a type the protocol does not declare is refused, naming it, rather
    ///     than reflected at run time.
    /// </summary>
    [Fact]
    public void JsonWireFormat_ATypeOutsideTheProtocol_IsRefused()
    {
        IWireFormat format = new JsonWireFormat();

        var thrown = Record.Exception(() => format.Serialize(new NotAProtocolType { Name = "x" }));

        thrown.Should().BeOfType<NotSupportedException>();
        thrown!.Message.Should().Contain(nameof(NotAProtocolType));
    }

    [Fact]
    public void JsonWireFormat_SerializeOptions_AreCamelCaseAndCaseInsensitive()
    {
        JsonWireFormat.SerializerOptions.PropertyNamingPolicy
            .Should().Be(System.Text.Json.JsonNamingPolicy.CamelCase);
        JsonWireFormat.SerializerOptions.PropertyNameCaseInsensitive.Should().BeTrue();
    }

    [Fact]
    public void JsonWireFormat_Instance_IsShared()
    {
        JsonWireFormat.Instance.Should().BeSameAs(JsonWireFormat.Instance);
    }
}
