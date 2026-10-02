using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Tests.Unit;

public class JsonMessageSerializerTests
{
    private readonly JsonMessageSerializer _serializer = new();

    public record TestMessage(string Name, int Value);

    [Fact]
    public void SerializeToString_ShouldProduceValidJson()
    {
        var message = new TestMessage("test", 42);

        var json = _serializer.SerializeToString(message);

        json.Should().Contain("\"name\"");
        json.Should().Contain("\"value\"");
        json.Should().Contain("42");
    }

    [Fact]
    public void DeserializeFromString_ShouldRoundTrip()
    {
        var original = new TestMessage("hello", 99);
        var json = _serializer.SerializeToString(original);

        var result = _serializer.DeserializeFromString<TestMessage>(json);

        result.Should().NotBeNull();
        result!.Name.Should().Be("hello");
        result.Value.Should().Be(99);
    }

    [Fact]
    public void Serialize_ShouldProduceBytes()
    {
        var message = new TestMessage("test", 1);

        var bytes = _serializer.Serialize(message);

        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Deserialize_ShouldRoundTripFromBytes()
    {
        var original = new TestMessage("world", 7);
        var bytes = _serializer.Serialize(original);

        var result = _serializer.Deserialize<TestMessage>(bytes);

        result.Should().NotBeNull();
        result!.Name.Should().Be("world");
        result.Value.Should().Be(7);
    }

    [Fact]
    public void SerializeToString_ShouldUseCamelCase()
    {
        var message = new TestMessage("test", 1);

        var json = _serializer.SerializeToString(message);

        json.Should().Contain("\"name\"");
        json.Should().NotContain("\"Name\"");
    }
}
