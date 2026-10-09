using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Serialization;

/// <summary>
///     A member typed <c>object</c> no longer sends the whole response to the serializer (#130): the writer writes the
///     rest, leaves the member out when it is null, and hands the value to the serializer, under the host's options,
///     when it holds something — whatever its runtime type.
/// </summary>
public class AnObjectMemberIsWrittenAsTheSerializerWritesItTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace App;

        public enum Mood { Calm, Loud }

        public sealed record Point(double X, double Y);

        public sealed class Post
        {
            public int Id { get; init; }
            public string Text { get; init; } = "";
            public object? Geo { get; init; }
            public object? Place { get; init; }
            public object[]? Symbols { get; init; }
            public List<object?> Extras { get; init; } = [];
        }

        public static class Samples
        {
            public static object Nulls() => new Post { Id = 1, Text = "t" };

            public static object Held() => new Post
            {
                Id = 2,
                Text = "<é>",
                Geo = new Point(1.5, -2),
                Place = "Forlì",
                Symbols = [1, "two", true, null!, Mood.Loud, new Dictionary<string, object?> { ["k"] = 3 }],
                Extras = [null, 4.25m, new object(), new { Name = "anon", When = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc) }],
            };
        }
        """;

    [Theory]
    [InlineData("Nulls")]
    [InlineData("Held")]
    public void AnObjectMember_IsWrittenAsTheSerializerWritesWhatItHolds(string sample)
    {
        var compiled = CompiledResponseWriter.For(Source, CompiledResponseWriter.Named("App.Post"));
        compiled.Rejection.Should().BeNull();

        var value = compiled.Sample(sample);

        compiled.Write(value).Should().Be(CompiledResponseWriter.Serialize(value, value.GetType(), excludesInfrastructure: false));
    }
}
