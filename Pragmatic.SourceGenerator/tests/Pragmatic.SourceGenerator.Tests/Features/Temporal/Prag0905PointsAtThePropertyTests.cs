using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Temporal;

/// <summary>
///     <c>PRAG0905</c> points at the declaration it is about.
/// </summary>
/// <remarks>
///     <para>
///         Reported with <c>Location.None</c>, the message alone would name the type and the property:
///         the IDE could not navigate to it and the build output would carry no file or line. In a
///         solution of a hundred DTOs, reading the message would be the whole search.
///     </para>
///     <para>
///         ⚠️ The other PRAG0905 test asserts that the diagnostic <em>is reported</em>, and a test shaped
///         that way cannot tell a located diagnostic from an unlocated one. That is why this is
///         separate: the claim is about the position, so the assertion has to be about the position.
///     </para>
/// </remarks>
public class Prag0905PointsAtThePropertyTests
{
    private const string Stubs = """
        namespace Pragmatic.Temporal.Clock { public class SystemClock { } }

        namespace Pragmatic.Temporal.Json.Behaviors
        {
            public enum TemporalJsonBehavior { AsUtc, FromClientTimezone, ToClientTimezone, FromBusinessTimezone, ToBusinessTimezone, KeepTimezone }
            public static class TemporalJsonBehaviorRegistry
            {
                public static void Register(System.Type dtoType, string propertyName, TemporalJsonBehavior behavior) { }
            }
        }

        namespace Pragmatic.Temporal.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class ToClientTimezoneAttribute : System.Attribute { }
        }
        """;

    private static Diagnostic TheDiagnostic(string source) =>
        GeneratorTestHelper.GetGeneratorDiagnostics(
                GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []), "PRAG0905")
            .Single();

    /// <summary>The reported position is inside the source, not nowhere.</summary>
    [Fact]
    public void TheWarning_CarriesAPositionInTheSource()
    {
        var diagnostic = TheDiagnostic(Stubs + """

            namespace App.Dtos
            {
                using Pragmatic.Temporal.Attributes;

                public class BadDto
                {
                    [ToClientTimezone]
                    public string Label { get; set; }
                }
            }
            """);

        diagnostic.Location.Should().NotBe(Location.None,
            "a warning nobody can navigate to is read by nobody");
        diagnostic.Location.GetLineSpan().Path.Should().Be("TestSource.cs");
    }

    /// <summary>
    ///     It points at the attribute, not at the file and not at the property beside it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Without this, "it has a location" is satisfied by pointing at line 1 of the file —
    ///         which is a position, is not <c>Location.None</c>, and helps nobody.
    ///     </para>
    ///     <para>
    ///         The attribute rather than the property, because the attribute is what the author would
    ///         remove, and because <c>PRAG0210</c> — the other diagnostic about an attribute that
    ///         generates nothing — already reports on the attribute's own syntax. Two diagnostics about
    ///         the same kind of mistake should land in the same place.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ThePosition_CoversTheOffendingProperty()
    {
        const string source = Stubs + """

            namespace App.Dtos
            {
                using Pragmatic.Temporal.Attributes;

                public class BadDto
                {
                    public string Untouched { get; set; }

                    [ToClientTimezone]
                    public string Label { get; set; }
                }
            }
            """;

        var diagnostic = TheDiagnostic(source);
        var span = diagnostic.Location.SourceSpan;
        var pointedAt = source.Substring(span.Start, span.Length);

        pointedAt.Should().Contain("ToClientTimezone",
            "the position is the attribute, which is the thing the author would remove");
        pointedAt.Should().NotContain("Untouched",
            "and nowhere near the property beside it");

        // The attribute sits on the line above the property it decorates, so the reported line is
        // one before Label's — which is what makes «go to the warning» land somewhere useful.
        var reportedLine = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        var labelLine = source.Split('\n').ToList().FindIndex(l => l.Contains("public string Label"));
        reportedLine.Should().Be(labelLine - 1);
    }

    /// <summary>
    ///     The control: with two offending properties, each is reported at its own place.
    /// </summary>
    /// <remarks>
    ///     Without it, a single captured position would satisfy both assertions above and put every
    ///     warning of a file on the same line — which is what an unlocated diagnostic already did,
    ///     only harder to notice.
    /// </remarks>
    [Fact]
    public void TwoOffendingProperties_AreReportedAtTwoPlaces()
    {
        var diagnostics = GeneratorTestHelper.GetGeneratorDiagnostics(
                GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + """

                    namespace App.Dtos
                    {
                        using Pragmatic.Temporal.Attributes;

                        public class BadDto
                        {
                            [ToClientTimezone]
                            public string First { get; set; }

                            [ToClientTimezone]
                            public string Second { get; set; }
                        }
                    }
                    """, []), "PRAG0905")
            .ToList();

        diagnostics.Should().HaveCount(2);
        diagnostics[0].Location.SourceSpan.Should().NotBe(diagnostics[1].Location.SourceSpan);
    }
}
