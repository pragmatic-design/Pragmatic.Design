using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Protocol.Serialization;
using Xunit;

namespace Pragmatic.Agent.Tests.Protocol;

/// <summary>
///     Serialization round-trip coverage for <see cref="JsonWireFormat" />, the live wire format used by
///     <see cref="Pragmatic.Agent.Protocol.FrameCodec" /> on the socket and gossip. It serializes plain
///     POCOs via property reflection, so it is exercised with a rich object (nested collections, nullable
///     members). <see cref="IWireFormat" /> is internal, so it is never exposed on a public test signature.
/// </summary>
/// <remarks>
///     A binary (MessagePack) format was prototyped but removed — the message model is JSON-native
///     (<c>AgentMessage.Payload</c> is a <c>JsonElement</c>), so a real binary format needs a
///     format-neutral envelope + self-describing frames. See
///     <c>docs/future/agent-transport-security-and-binary-wireformat.md</c>.
/// </remarks>
public class WireFormatTests
{
    public sealed class Sample
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public bool Flag { get; set; }
        public string? Optional { get; set; }
        public List<string> Items { get; set; } = [];
    }

    [Fact]
    public void JsonWireFormat_FormatName_IsJson() =>
        new JsonWireFormat().FormatName.Should().Be("json");

    [Fact]
    public void JsonWireFormat_PopulatedObject_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();
        var original = new Sample
        {
            Name = "node-1",
            Count = 42,
            Flag = true,
            Optional = "present",
            Items = ["a", "b", "c"]
        };

        var decoded = format.Deserialize<Sample>(format.Serialize(original));

        decoded.Should().NotBeNull();
        decoded!.Name.Should().Be("node-1");
        decoded.Count.Should().Be(42);
        decoded.Flag.Should().BeTrue();
        decoded.Optional.Should().Be("present");
        decoded.Items.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void JsonWireFormat_NullOptional_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();

        var decoded = format.Deserialize<Sample>(format.Serialize(new Sample { Name = "x", Optional = null }));

        decoded.Should().NotBeNull();
        decoded!.Optional.Should().BeNull();
    }

    [Fact]
    public void JsonWireFormat_EmptyCollection_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();

        var decoded = format.Deserialize<Sample>(format.Serialize(new Sample { Name = "x", Items = [] }));

        decoded.Should().NotBeNull();
        decoded!.Items.Should().BeEmpty();
    }

    [Fact]
    public void JsonWireFormat_PrimitiveString_RoundTrips()
    {
        IWireFormat format = new JsonWireFormat();

        format.Deserialize<string>(format.Serialize("hello")).Should().Be("hello");
    }

    [Fact]
    public void JsonWireFormat_Serialize_ProducesNonEmptyPayload()
    {
        new JsonWireFormat().Serialize(new Sample { Name = "x" }).Should().NotBeEmpty();
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
