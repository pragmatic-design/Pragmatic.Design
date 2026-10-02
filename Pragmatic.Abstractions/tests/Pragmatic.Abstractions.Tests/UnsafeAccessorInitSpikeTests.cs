using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     W3-P2d spike #2: can <c>[UnsafeAccessor]</c> (AOT-safe, reflection-free) drive the <c>init</c>
///     setters and parameterized ctors that the writable <see cref="JsonTypeInfo"/> API (the WORKING
///     path: <c>CreateJsonTypeInfo</c> + <c>Properties.Add</c>) can't reach in C#? If yes, records and
///     init-only types become coverable by the generated context.
/// </summary>
public sealed class UnsafeAccessorInitSpikeTests
{
    public sealed class InitDto
    {
        public string Name { get; init; } = "";
        public int Count { get; init; }
    }

    public sealed record RecDto(string Name, int Count);

    // AOT-safe, reflection-free accessors to the init setters (bypass the C# init-only rule at call time).
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Name")]
    private static extern void SetInitName(InitDto instance, string value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Count")]
    private static extern void SetInitCount(InitDto instance, int value);

    // Reach the record's primary constructor and its (init) positional setters.
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern RecDto NewRec(string name, int count);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Name")]
    private static extern void SetRecName(RecDto instance, string value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Count")]
    private static extern void SetRecCount(RecDto instance, int value);

    private sealed class UaContext : JsonSerializerContext, IJsonTypeInfoResolver
    {
        public UaContext() : base(null) { }
        protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

        public override JsonTypeInfo? GetTypeInfo(Type type)
            => ((IJsonTypeInfoResolver)this).GetTypeInfo(type, Options);

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(InitDto)) return CreateInitInfo(options);
            if (type == typeof(RecDto)) return CreateRecInfo(options);
            if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);
            if (type == typeof(int)) return JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter);
            return null;
        }

        private static JsonTypeInfo<InitDto> CreateInitInfo(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<InitDto>(options);
            info.CreateObject = static () => new InitDto();

            var name = info.CreateJsonPropertyInfo(typeof(string), "name");
            name.Get = static o => ((InitDto)o).Name;
            name.Set = static (o, v) => SetInitName((InitDto)o, (string)v!);
            info.Properties.Add(name);

            var count = info.CreateJsonPropertyInfo(typeof(int), "count");
            count.Get = static o => ((InitDto)o).Count;
            count.Set = static (o, v) => SetInitCount((InitDto)o, (int)v!);
            info.Properties.Add(count);

            return info;
        }

        private static JsonTypeInfo<RecDto> CreateRecInfo(JsonSerializerOptions options)
        {
            var info = JsonTypeInfo.CreateJsonTypeInfo<RecDto>(options);
            // No parameterless ctor: construct via the primary ctor (defaults), then STJ sets the
            // positional (init) properties via the UnsafeAccessor setters below.
            info.CreateObject = static () => NewRec(null!, 0);

            var name = info.CreateJsonPropertyInfo(typeof(string), "name");
            name.Get = static o => ((RecDto)o).Name;
            name.Set = static (o, v) => SetRecName((RecDto)o, (string)v!);
            info.Properties.Add(name);

            var count = info.CreateJsonPropertyInfo(typeof(int), "count");
            count.Get = static o => ((RecDto)o).Count;
            count.Set = static (o, v) => SetRecCount((RecDto)o, (int)v!);
            info.Properties.Add(count);

            return info;
        }
    }

    [Fact]
    public void InitOnly_ThroughSeam_RoundTrips()
    {
        var options = new PragmaticJsonOptions()
            .AddContext(new UaContext())
            .DisableReflectionFallback()
            .Build();

        var json = JsonSerializer.Serialize(new InitDto { Name = "z", Count = 4 }, options);
        json.Should().Contain("\"name\"").And.Contain("\"z\"").And.Contain("\"count\"");

        var back = JsonSerializer.Deserialize<InitDto>(json, options);
        back.Should().NotBeNull();
        back!.Name.Should().Be("z");
        back.Count.Should().Be(4);
    }

    [Fact]
    public void Record_ThroughSeam_RoundTrips()
    {
        var options = new PragmaticJsonOptions()
            .AddContext(new UaContext())
            .DisableReflectionFallback()
            .Build();

        var json = JsonSerializer.Serialize(new RecDto("acme", 7), options);
        json.Should().Contain("\"name\"").And.Contain("\"acme\"").And.Contain("\"count\"");

        var back = JsonSerializer.Deserialize<RecDto>(json, options);
        back.Should().NotBeNull();
        back!.Name.Should().Be("acme");
        back.Count.Should().Be(7);
    }
}
