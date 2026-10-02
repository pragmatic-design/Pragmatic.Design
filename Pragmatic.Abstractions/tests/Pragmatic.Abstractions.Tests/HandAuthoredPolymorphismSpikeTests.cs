using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     W3-P4 exploration: can a [JsonPolymorphic] base with derived types be hand-authored via the manual
///     JsonTypeInfo API (info.PolymorphismOptions) and round-trip AOT-safely under the seam? Determines
///     whether the SG can emit polymorphism.
/// </summary>
public sealed class HandAuthoredPolymorphismSpikeTests
{
    public abstract class Shape { public string? Kind { get; set; } }
    public sealed class Circle : Shape { public double Radius { get; set; } }
    public sealed class Square : Shape { public double Side { get; set; } }

    private sealed class ShapeContext : JsonSerializerContext, IJsonTypeInfoResolver
    {
        public ShapeContext() : base(null) { }
        protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
        public override JsonTypeInfo? GetTypeInfo(Type type) => ((IJsonTypeInfoResolver)this).GetTypeInfo(type, Options);

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);
            if (type == typeof(double)) return JsonMetadataServices.CreateValueInfo<double>(options, JsonMetadataServices.DoubleConverter);
            if (type == typeof(Shape)) return CreateShape(options);
            if (type == typeof(Circle)) return CreateCircle(options);
            if (type == typeof(Square)) return CreateSquare(options);
            return null;
        }

        private static JsonTypeInfo<Shape> CreateShape(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<Shape>(options);
            // Abstract base: no CreateObject; configure polymorphism.
            info.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(Circle), "circle"),
                    new JsonDerivedType(typeof(Square), "square"),
                },
            };
            var kind = info.CreateJsonPropertyInfo(typeof(string), "kind");
            kind.Get = static o => ((Shape)o).Kind; kind.Set = static (o, v) => ((Shape)o).Kind = (string?)v!;
            info.Properties.Add(kind);
            return info;
        }

        private static JsonTypeInfo<Circle> CreateCircle(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<Circle>(options);
            info.CreateObject = static () => new Circle();
            var r = info.CreateJsonPropertyInfo(typeof(double), "radius");
            r.Get = static o => ((Circle)o).Radius; r.Set = static (o, v) => ((Circle)o).Radius = (double)v!;
            info.Properties.Add(r);
            return info;
        }

        private static JsonTypeInfo<Square> CreateSquare(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<Square>(options);
            info.CreateObject = static () => new Square();
            var s = info.CreateJsonPropertyInfo(typeof(double), "side");
            s.Get = static o => ((Square)o).Side; s.Set = static (o, v) => ((Square)o).Side = (double)v!;
            info.Properties.Add(s);
            return info;
        }
    }

    [Fact]
    public void Polymorphic_RoundTripsUnderSeam_FallbackOff()
    {
        var options = new PragmaticJsonOptions().AddContext(new ShapeContext()).DisableReflectionFallback().Build();

        Shape original = new Circle { Kind = "c", Radius = 2.5 };
        var json = JsonSerializer.Serialize(original, options);
        var back = JsonSerializer.Deserialize<Shape>(json, options);

        json.Should().Contain("\"$type\":\"circle\"");
        back.Should().BeOfType<Circle>();
        ((Circle)back!).Radius.Should().Be(2.5);
    }
}
