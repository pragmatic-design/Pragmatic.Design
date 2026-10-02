using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     W3 re-test: a hand-authored <see cref="JsonSerializerContext"/> SUBCLASS (no <c>[JsonSerializable]</c>,
///     no STJ source generator involved) whose <c>JsonTypeInfo</c> bodies are built with
///     <see cref="JsonMetadataServices"/> — i.e. exactly what the Pragmatic SG could emit at the host
///     from its own metadata. Verifies it serializes correctly under the seam's camelCase options.
///     If this passes, W3 IS viable and the "not viable" conclusion (based on a standalone resolver)
///     was wrong.
/// </summary>
public sealed class HandAuthoredContextSpikeTests
{
    public sealed class Dto
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    // Stand-in for SG output: a real JsonSerializerContext subclass we author entirely ourselves.
    // It re-implements IJsonTypeInfoResolver so that, when combined into another options' chain, it
    // builds the JsonTypeInfo bound to the PASSED options (exactly what the STJ source generator does).
    private sealed class HandContext : JsonSerializerContext, IJsonTypeInfoResolver
    {
        public HandContext() : base(null) { }

        protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

        public override JsonTypeInfo? GetTypeInfo(Type type)
            => ((IJsonTypeInfoResolver)this).GetTypeInfo(type, Options);

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            // An SG-emitted context provides value metadata for every primitive its properties use.
            if (type == typeof(Dto)) return CreateDtoInfo(options);
            if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);
            if (type == typeof(int)) return JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter);
            return null;
        }

        private static JsonTypeInfo<Dto> CreateDtoInfo(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<Dto>(options);
            info.CreateObject = static () => new Dto();

            var name = info.CreateJsonPropertyInfo(typeof(string), "name");
            name.Get = static obj => ((Dto)obj).Name;
            name.Set = static (obj, value) => ((Dto)obj).Name = (string?)value;
            info.Properties.Add(name);

            var count = info.CreateJsonPropertyInfo(typeof(int), "count");
            count.Get = static obj => ((Dto)obj).Count;
            count.Set = static (obj, value) => ((Dto)obj).Count = (int)value!;
            info.Properties.Add(count);

            return info;
        }
    }

    [Fact]
    public void HandAuthoredContextSubclass_InSeam_RoundTripsWithFallbackOff()
    {
        var options = new PragmaticJsonOptions()
            .AddContext(new HandContext())
            .DisableReflectionFallback()
            .Build();

        var json = JsonSerializer.Serialize(new Dto { Name = "x", Count = 3 }, options);
        json.Should().Contain("\"name\"").And.Contain("\"count\"");

        var back = JsonSerializer.Deserialize<Dto>(json, options);
        back.Should().NotBeNull();
        back!.Name.Should().Be("x");
        back.Count.Should().Be(3);
    }
}
