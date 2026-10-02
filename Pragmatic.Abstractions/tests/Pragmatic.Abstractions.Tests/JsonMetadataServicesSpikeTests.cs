using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     W3 spike (characterization of what does NOT work): a <see cref="JsonTypeInfo{T}"/> built via
///     <see cref="JsonMetadataServices"/> and returned from a bare STANDALONE
///     <see cref="IJsonTypeInfoResolver"/> does not serialize under the seam's camelCase options —
///     the metadata-only property path is not populated outside a
///     <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> host. The WORKING shape —
///     a hand-authored JsonSerializerContext subclass (which the Pragmatic SG can emit from its host
///     metadata, with NO STJ generator involved) — is in <see cref="HandAuthoredContextSpikeTests"/>.
///     Together they show why W3 must emit a context subclass, not a bare resolver.
///     See <c>docs/future/aot-w3-jsonmetadataservices-spike-2026-07.md</c>.
/// </summary>
public sealed class JsonMetadataServicesSpikeTests
{
    public sealed class SpikeDto
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    // Stand-in for SG output: a resolver that returns metadata-services-built JsonTypeInfo.
    private sealed class SpikeResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            => type == typeof(SpikeDto) ? CreateSpikeDtoInfo(options) : null;

        private static JsonTypeInfo<SpikeDto> CreateSpikeDtoInfo(JsonSerializerOptions options)
        {
            var info = JsonMetadataServices.CreateObjectInfo<SpikeDto>(options, new JsonObjectInfoValues<SpikeDto>
            {
                ObjectCreator = static () => new SpikeDto(),
                // Fast-path serialize handler works for a standalone resolver; the metadata-only path
                // (properties without a handler) does NOT populate outside a JsonSerializerContext host.
                SerializeHandler = static (writer, value) =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", value.Name);
                    writer.WriteNumber("count", value.Count);
                    writer.WriteEndObject();
                },
                PropertyMetadataInitializer = _ =>
                [
                    JsonMetadataServices.CreatePropertyInfo(options, new JsonPropertyInfoValues<string?>
                    {
                        DeclaringType = typeof(SpikeDto),
                        PropertyName = nameof(SpikeDto.Name),
                        Getter = static obj => ((SpikeDto)obj).Name,
                        Setter = static (obj, value) => ((SpikeDto)obj).Name = value,
                    }),
                    JsonMetadataServices.CreatePropertyInfo(options, new JsonPropertyInfoValues<int>
                    {
                        DeclaringType = typeof(SpikeDto),
                        PropertyName = nameof(SpikeDto.Count),
                        Getter = static obj => ((SpikeDto)obj).Count,
                        Setter = static (obj, value) => ((SpikeDto)obj).Count = value,
                    }),
                ],
            });
            return info;
        }
    }

    [Fact]
    public void StandaloneMetadataServicesResolver_UnderSeamOptions_DoesNotPopulateProperties()
    {
        var options = new PragmaticJsonOptions()
            .AddContext(new SpikeResolver())
            .DisableReflectionFallback()
            .Build();

        var json = JsonSerializer.Serialize(new SpikeDto { Name = "x", Count = 3 }, options);

        // The limitation: no properties are emitted (empty object), proving standalone
        // JsonMetadataServices is not a viable W3 path. The type IS resolved (no throw / no fallback).
        json.Should().Be("{}");
    }
}
