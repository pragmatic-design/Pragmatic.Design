using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     A generated response writer writes, byte for byte, what <c>JsonSerializer</c> writes for the same value under
///     the options the generated entry point gives the host.
/// </summary>
/// <remarks>
///     The oracle is the serializer itself, with the reflection resolver: what a host answers with for a type no
///     generated context covers. Every value below is chosen to take a branch: a null member left out and one
///     written, an enum value with a name and one without, text the default encoder escapes, an acronym the naming
///     policy treats differently from the first letter.
/// </remarks>
public class AResponseWriterWritesWhatTheHostSerializerWritesTests
{
    private const string Dto = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;

        namespace App;

        public enum Status { Draft, Open, Closed = 7 }

        public enum Tiny : byte { Low = 1, High = 200 }

        public sealed record Line(string Sku, int Quantity, decimal Price);

        public record struct Point(double X, double Y);

        public class AuditedBase
        {
            public DateTimeOffset CreatedAt { get; init; }
            public string? CreatedBy { get; init; }
        }

        public sealed class Order : AuditedBase
        {
            public Guid Id { get; init; }
            public string Number { get; init; } = "";
            public string? Note { get; init; }
            public string URLPath { get; init; } = "";
            public string IOStream { get; init; } = "";
            public bool IsUrgent { get; init; }
            public char Grade { get; init; }
            public short Small { get; init; }
            public byte Octet { get; init; }
            public long Big { get; init; }
            public ulong Huge { get; init; }
            public float Ratio { get; init; }
            public double Score { get; init; }
            public decimal Total { get; init; }
            public int? Rank { get; init; }
            public Status Status { get; init; }
            public Status? Previous { get; init; }
            public Tiny Level { get; init; }
            public DateTime PlacedAt { get; init; }
            public DateOnly DueOn { get; init; }
            public TimeOnly Slot { get; init; }
            public TimeSpan Window { get; init; }
            public DateTimeOffset? ShippedAt { get; init; }
            public Uri? Link { get; init; }
            public byte[]? Signature { get; init; }
            public Point Location { get; init; }
            public Point? Destination { get; init; }
            public Line? Main { get; init; }
            public List<Line> Lines { get; init; } = [];
            public IReadOnlyList<string?> Tags { get; init; } = [];
            public int[] Codes { get; init; } = [];
            public Dictionary<string, int?> Counters { get; init; } = new();
            public Dictionary<int, string> ByNumber { get; init; } = new();
            public IReadOnlyDictionary<ulong, Line> ByCode { get; init; } = new Dictionary<ulong, Line>();
            public IEnumerable<Status> History { get; init; } = [];

            [JsonPropertyName("external_ref")]
            public string? ExternalReference { get; init; }

            [JsonPropertyName("città <€> \"q\"")]
            public string? City { get; init; }

            [JsonIgnore]
            public string Secret { get; init; } = "hidden";

            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public string? AlwaysThere { get; init; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int Revision { get; init; }

            [JsonPropertyOrder(-1)]
            public string First { get; init; } = "first";

            public int LineCount => Lines.Count;
        }

        public static class Samples
        {
            public static object Full() => new Order
            {
                CreatedAt = new DateTimeOffset(2026, 10, 8, 9, 30, 15, 120, TimeSpan.FromHours(2)),
                CreatedBy = "anna",
                Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                Number = "N-<1>&\"é\" \u0001 \U0001F600",
                Note = "a note",
                URLPath = "/x",
                IOStream = "s",
                IsUrgent = true,
                Grade = '<',
                Small = -3,
                Octet = 255,
                Big = long.MinValue,
                Huge = ulong.MaxValue,
                Ratio = 0.1f,
                Score = 1.0 / 3,
                Total = 12.3400m,
                Rank = 4,
                Status = Status.Closed,
                Previous = (Status)42,
                Level = Tiny.High,
                PlacedAt = new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc),
                DueOn = new DateOnly(2026, 12, 31),
                Slot = new TimeOnly(14, 5, 9, 250),
                Window = TimeSpan.FromMinutes(-90.5),
                ShippedAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
                Link = new Uri("https://example.com/a b?q=<x>", UriKind.Absolute),
                Signature = [1, 2, 250],
                Location = new Point(1.5, -2),
                Destination = new Point(3, 4),
                Main = new Line("A-1", 2, 9.99m),
                Lines = [new Line("B-2", 1, 0.5m), new Line("C-3", 0, 0m)],
                Tags = ["x", null, "z"],
                Codes = [3, 2, 1],
                Counters = new() { ["a"] = 1, ["<b>"] = null },
                ByNumber = new() { [-7] = "minus", [0] = "zero", [int.MaxValue] = "max" },
                ByCode = new Dictionary<ulong, Line> { [ulong.MaxValue] = new Line("Z", 1, 1m) },
                History = [Status.Draft, (Status)99],
                ExternalReference = "ext",
                City = "Forlì",
                Revision = 3,
            };

            public static object Empty() => new Order();
        }
        """;

    [Theory]
    [InlineData("Full")]
    [InlineData("Empty")]
    public void AnObject_IsWrittenAsTheSerializerWritesIt(string sample)
    {
        var compiled = CompiledResponseWriter.For(Dto, CompiledResponseWriter.Named("App.Order"));
        compiled.Rejection.Should().BeNull();

        var value = compiled.Sample(sample);

        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: false));
    }
}
