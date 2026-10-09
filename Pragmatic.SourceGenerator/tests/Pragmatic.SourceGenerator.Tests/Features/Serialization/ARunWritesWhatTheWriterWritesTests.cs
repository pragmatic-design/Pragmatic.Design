using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     The parts of a response no encoder can change are written as runs, one raw value each (#131), and the bytes
///     are still the serializer's: separators, nulls left out, defaults left out, dates trimmed, a run that outgrows
///     its stack buffer.
/// </summary>
public class ARunWritesWhatTheWriterWritesTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text.Json.Serialization;

        namespace App;

        public sealed record Area(int AreaId, int[]? BlockIds);

        public sealed record Seat(int SeatCategoryId, Area?[]? Areas);

        public sealed class Stamp
        {
            public DateTime Unspecified { get; init; }
            public DateTime Utc { get; init; }
            public DateTimeOffset Offset { get; init; }
            public DateTimeOffset? Missing { get; init; }
        }

        public sealed class Ticket
        {
            public int Id { get; init; }
            public short Small { get; init; }
            public long Start { get; init; }
            public uint Count { get; init; }
            public ulong Huge { get; init; }
            public decimal Price { get; init; }
            public double Score { get; init; }
            public float Ratio { get; init; }
            public double[][]? Shape { get; init; }
            public bool Paid { get; init; }
            public Guid Ref { get; init; }
            public int? Rank { get; init; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Revision { get; init; }
            public string? Title { get; init; }
            public Stamp? When { get; init; }
            public List<Seat>? Seats { get; init; }
            public Dictionary<int, Area?>? ByArea { get; init; }
            public Dictionary<long, int[]?>? Topics { get; init; }
            public string Venue { get; init; } = "";
            public int Last { get; init; }
            public bool Done { get; init; }
        }

        public static class Samples
        {
            public static object Full() => new Ticket
            {
                Id = -1,
                Small = short.MinValue,
                Start = long.MaxValue,
                Count = uint.MaxValue,
                Huge = ulong.MaxValue,
                Price = -12.3400m,
                Score = 1.0 / 3,
                Ratio = 0.1f,
                Shape = [[1e-7, 1e21, -0.0, double.MaxValue, double.Epsilon], [0.1, 2, -71.05]],
                Paid = true,
                Ref = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                Rank = 0,
                Revision = 2,
                Title = "<é>",
                When = new Stamp
                {
                    Unspecified = new DateTime(2026, 10, 9, 8, 7, 6),
                    Utc = new DateTime(2026, 10, 9, 8, 7, 6, DateTimeKind.Utc).AddTicks(1_200_000),
                    Offset = new DateTimeOffset(2026, 10, 9, 8, 7, 6, TimeSpan.FromMinutes(-150)).AddTicks(1_234_567),
                },
                Seats = [new Seat(1, [new Area(10, [1, 2, 3]), null, new Area(11, null)]), new Seat(2, null)],
                ByArea = new() { [-5] = new Area(5, []), [7] = null },
                Topics = new() { [long.MinValue] = [9], [0] = null },
                Venue = "Forlì",
                Last = 3,
                Done = false,
            };

            public static object Empty() => new Ticket();

            // Two hundred areas of five blocks: a run several times the size of its stack buffer.
            public static object Large() => new Ticket
            {
                Seats = [new Seat(1, [.. Enumerable.Range(0, 200).Select(i => new Area(i, [i, i + 1, i + 2, i + 3, i + 4]))])],
            };

            public static object NotFinite() => new Ticket { Score = double.NaN };

            public static object Rows() => (IReadOnlyList<Area?>)new List<Area?> { new(1, [2]), null, new(3, null) };
        }
        """;

    [Theory]
    [InlineData("Full")]
    [InlineData("Empty")]
    [InlineData("Large")]
    public void AnObjectWrittenInRuns_IsWrittenAsTheSerializerWritesIt(string sample)
    {
        var compiled = CompiledResponseWriter.For(Source, CompiledResponseWriter.Named("App.Ticket"));
        compiled.Rejection.Should().BeNull();

        var value = compiled.Sample(sample);

        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: false));
    }

    /// <summary>
    ///     The control for the test above: the writer does write runs — for the members before the string, the members
    ///     after it, and every object made only of encoder-free values — so the bytes it is compared on come from them.
    /// </summary>
    [Fact]
    public void TheEncoderFreeParts_AreWrittenAsRuns()
    {
        var compiled = CompiledResponseWriter.For(Source, CompiledResponseWriter.Named("App.Ticket"));

        compiled.Generated.Should().Contain("internal static void Run_App_Area(ref global::Pragmatic.Serialization.Utf8JsonRun run");
        compiled.Generated.Should().Contain("internal static void Run_App_Seat(ref global::Pragmatic.Serialization.Utf8JsonRun run");
        compiled.Generated.Should().Contain("internal static void Run_App_Stamp(ref global::Pragmatic.Serialization.Utf8JsonRun run");
        compiled.Generated.Should().NotContain("Run_App_Ticket(");
        compiled.Generated.Split("writer.WriteRawValue(run.Written, skipInputValidation: true);").Length.Should().BeGreaterThan(2);
    }

    /// <summary>A number JSON cannot hold fails the response through the run as it fails through the serializer.</summary>
    [Fact]
    public void ANumberThatIsNotFinite_IsRefusedAsTheSerializerRefusesIt()
    {
        var compiled = CompiledResponseWriter.For(Source, CompiledResponseWriter.Named("App.Ticket"));
        var value = compiled.Sample("NotFinite");

        var throughTheSerializer = Record.Exception(() => CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: false));
        var throughTheRun = Record.Exception(() => compiled.Write(value));

        throughTheSerializer.Should().BeOfType<ArgumentException>();
        (throughTheRun is System.Reflection.TargetInvocationException { InnerException: ArgumentException }).Should().BeTrue();
    }

    /// <summary>A list of encoder-free objects, as a query answers with: the whole response is one run.</summary>
    [Fact]
    public void AListOfEncoderFreeRows_IsOneRun()
    {
        var compiled = CompiledResponseWriter.For(Source, compilation => compilation
            .GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1")!
            .Construct(compilation.GetTypeByMetadataName("App.Area")!));
        compiled.Rejection.Should().BeNull();

        var value = compiled.Sample("Rows");
        var declared = typeof(IReadOnlyList<>).MakeGenericType(value.GetType().GetGenericArguments()[0]);

        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, declared, excludesInfrastructure: false));
        compiled.Generated.Should().Contain("run.StartArray();");
    }
}
