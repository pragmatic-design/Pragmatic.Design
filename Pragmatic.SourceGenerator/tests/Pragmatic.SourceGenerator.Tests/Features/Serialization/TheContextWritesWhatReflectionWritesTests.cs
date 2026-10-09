using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     The JSON context the generator emits writes a type as System.Text.Json's reflection resolver does under the
///     host's options: which of the two answers depends on whether a module opted in to the context (or publishes
///     AOT), and that must not change the document a client receives.
/// </summary>
public class TheContextWritesWhatReflectionWritesTests
{
    private const string Source = """
        using System.Text.Json.Serialization;

        namespace Wire;

        public class Device
        {
            public string URL { get; set; } = "";
            public string IOStream { get; set; } = "";
            public string Name { get; set; } = "";

            [JsonPropertyName("serial_no")]
            public string SerialNumber { get; set; } = "";

            [JsonIgnore]
            public string Secret { get; set; } = "";

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Count { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public string? Note { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Label { get; set; }
        }

        public static class Samples
        {
            public static Device Empty() => new() { URL = "u", IOStream = "s", Name = "n", SerialNumber = "1", Secret = "x" };

            public static Device Full() => new()
            {
                URL = "u", IOStream = "s", Name = "n", SerialNumber = "1", Secret = "x", Count = 3, Note = "hi", Label = "l",
            };
        }
        """;

    [Theory]
    [InlineData("Empty")]
    [InlineData("Full")]
    public void ADeviceIsWrittenTheSameWayByBoth(string sample)
    {
        var compiled = CompiledJsonContext.For(Source, "Wire.Device");
        var value = compiled.Sample(sample);

        compiled.WriteThroughTheContext(value)
            .Should().Be(CompiledJsonContext.WriteThroughReflection(value), compiled.Generated);
    }
}
