using System.Globalization;
using Microsoft.CodeAnalysis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Where a mapping diagnostic points.
/// </summary>
/// <remarks>
///     <para>
///         Every one of the thirty-five was reported at <c>Location.None</c>. For an error that is an
///         annoyance — the build says which type, not which line. For <c>PRAG0325</c>, which is
///         <c>Hidden</c>, it meant nowhere at all: not in the build output, by severity, and not in
///         the editor, for want of a position. The model was built from the symbols the whole time;
///         the position was there and thrown away.
///     </para>
///     <para>
///         The snapshot is the visible form of the change: a diagnostic line with a file and a
///         position where there was none.
///     </para>
/// </remarks>
public class DiagnosticLocationTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping.Attributes;

        namespace TestApp
        {
            public class Order
            {
                public string Reference { get; set; } = "";
            }

            [MapFrom<Order>]
            public partial record OrderDto
            {
                public string Reference { get; init; } = "";

                public string Missing { get; init; } = "";
            }
        }
        """;

    /// <summary>A diagnostic about a property points at the property.</summary>
    [Fact]
    public void APropertyDiagnostic_PointsAtTheProperty()
    {
        var diagnostic = GetDiagnosticsById(RunGenerator(Source), "PRAG0303").Single();

        diagnostic.Location.Should().NotBe(Location.None,
            "the model is built from the property symbol, which knows where it is");

        var span = diagnostic.Location.GetLineSpan();
        span.Path.Should().Be("TestSource.cs");
        Source.Split('\n')[span.StartLinePosition.Line].Should().Contain("Missing",
            "the line reported is the line of the property the diagnostic is about");
    }

    /// <summary>
    ///     A diagnostic about a type points at the type. PRAG0319 (a customization hook a projection
    ///     cannot run) is one; PRAG0300 is the companion analyzer's, not the generator's.
    /// </summary>
    [Fact]
    public void ATypeDiagnostic_PointsAtTheType()
    {
        var source = Source
            .Replace("[MapFrom<Order>]", "[MapFrom<Order>]\n    [GenerateProjection]")
            .Replace(
                "public string Missing { get; init; } = \"\";",
                "static partial void CustomizeMapping(Order source, ref OrderDtoMappingContext ctx);");

        var diagnostic = GetDiagnosticsById(RunGenerator(source), "PRAG0319").Single();

        diagnostic.Location.Should().NotBe(Location.None);

        var span = diagnostic.Location.GetLineSpan();
        span.Path.Should().Be("TestSource.cs");
        source.Split('\n')[span.StartLinePosition.Line].Should().Contain("OrderDto");
    }

    /// <summary>
    ///     The reverse-coverage diagnostic, <c>Hidden</c>, finally has somewhere to appear.
    /// </summary>
    [Fact]
    public void TheHiddenDiagnostic_HasAPositionToo()
    {
        var source = Source.Replace("public string Missing { get; init; } = \"\";", "")
            .Replace("public string Reference { get; init; } = \"\";", "");

        var diagnostic = GetDiagnosticsById(RunGenerator(source), "PRAG0325").Single();

        diagnostic.Location.Should().NotBe(Location.None,
            "Hidden and without a position is the one combination that shows up nowhere");
        source.Split('\n')[diagnostic.Location.GetLineSpan().StartLinePosition.Line]
            .Should().Contain("OrderDto", "a dropped source property is reported on the DTO that drops it");
    }

    [Fact]
    public async Task WhereTheDiagnosticsPoint()
    {
        // The partial source: a non-partial one is PRAG0300, which is the companion analyzer's and not
        // the generator's, and would leave this snapshot empty.
        var result = RunGenerator(Source);

        var lines = GetGeneratorDiagnostics(result)
            .Select(d =>
            {
                var span = d.Location.GetLineSpan();
                return $"{d.Id} at {span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): "
                       + d.GetMessage(CultureInfo.InvariantCulture);
            })
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        await Verify(lines);
    }
}
