using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     Verifies the exact JsonMetadataServices shape the SG must emit for collection properties
///     (List&lt;T&gt;, T[], Dictionary&lt;string,V&gt;) so the generator can mirror it. Round-trips under the
///     seam with the reflection fallback OFF.
/// </summary>
public sealed class HandAuthoredCollectionSpikeTests
{
    public sealed class Bag
    {
        public List<string>? Tags { get; set; }
        public int[]? Numbers { get; set; }
        public Dictionary<string, int>? Counts { get; set; }
        public int? Optional { get; set; }
    }

    private sealed class BagContext : JsonSerializerContext, IJsonTypeInfoResolver
    {
        public BagContext() : base(null) { }
        protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

        public override JsonTypeInfo? GetTypeInfo(Type type)
            => ((IJsonTypeInfoResolver)this).GetTypeInfo(type, Options);

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(Bag)) return CreateBag(options);
            if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);
            if (type == typeof(int)) return JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter);
            if (type == typeof(List<string>)) return JsonMetadataServices.CreateListInfo<List<string>, string>(options, new JsonCollectionInfoValues<List<string>> { ObjectCreator = static () => new List<string>() });
            if (type == typeof(int[])) return JsonMetadataServices.CreateArrayInfo<int>(options, new JsonCollectionInfoValues<int[]>());
            if (type == typeof(Dictionary<string, int>)) return JsonMetadataServices.CreateDictionaryInfo<Dictionary<string, int>, string, int>(options, new JsonCollectionInfoValues<Dictionary<string, int>> { ObjectCreator = static () => new Dictionary<string, int>() });
            if (type == typeof(int?)) return JsonMetadataServices.CreateValueInfo<int?>(options, JsonMetadataServices.GetNullableConverter<int>(options));
            return null;
        }

        private static JsonTypeInfo<Bag> CreateBag(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<Bag>(options);
            info.CreateObject = static () => new Bag();
            var tags = info.CreateJsonPropertyInfo(typeof(List<string>), "tags");
            tags.Get = static o => ((Bag)o).Tags; tags.Set = static (o, v) => ((Bag)o).Tags = (List<string>?)v;
            info.Properties.Add(tags);
            var numbers = info.CreateJsonPropertyInfo(typeof(int[]), "numbers");
            numbers.Get = static o => ((Bag)o).Numbers; numbers.Set = static (o, v) => ((Bag)o).Numbers = (int[]?)v;
            info.Properties.Add(numbers);
            var counts = info.CreateJsonPropertyInfo(typeof(Dictionary<string, int>), "counts");
            counts.Get = static o => ((Bag)o).Counts; counts.Set = static (o, v) => ((Bag)o).Counts = (Dictionary<string, int>?)v;
            info.Properties.Add(counts);
            var optional = info.CreateJsonPropertyInfo(typeof(int?), "optional");
            optional.Get = static o => ((Bag)o).Optional; optional.Set = static (o, v) => ((Bag)o).Optional = (int?)v;
            info.Properties.Add(optional);
            return info;
        }
    }

    [Fact]
    public void Collections_RoundTripUnderSeam_FallbackOff()
    {
        var options = new PragmaticJsonOptions().AddContext(new BagContext()).DisableReflectionFallback().Build();

        var original = new Bag
        {
            Tags = ["a", "b"],
            Numbers = [1, 2, 3],
            Counts = new Dictionary<string, int> { ["x"] = 9 },
            Optional = 42,
        };

        var json = JsonSerializer.Serialize(original, options);
        var back = JsonSerializer.Deserialize<Bag>(json, options);

        back.Should().NotBeNull();
        back!.Tags.Should().Equal("a", "b");
        back.Numbers.Should().Equal(1, 2, 3);
        back.Counts.Should().ContainKey("x").WhoseValue.Should().Be(9);
        back.Optional.Should().Be(42);
    }
}
