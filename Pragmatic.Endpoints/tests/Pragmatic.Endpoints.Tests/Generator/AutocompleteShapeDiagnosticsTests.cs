using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     PRAG0550: <c>[Autocomplete]</c> on a property that is not a string.
/// </summary>
/// <remarks>
///     The generated endpoint filters with <c>Contains()</c>, which only a string has. The descriptor
///     and the transform branch both existed; no test asserted that the branch was reached.
/// </remarks>
public class AutocompleteShapeDiagnosticsTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Autocomplete_OnANonStringProperty_ReportsPrag0550()
    {
        var source = """
            using Pragmatic.Endpoints.Attributes;

            namespace TestApp.Entities
            {
                public class Product
                {
                    public int Id { get; set; }
                    [Autocomplete] public int Code { get; set; }
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0550").Should().BeTrue("an int has no Contains() to filter with");
        GetGeneratedSource(result, "Autocomplete").Should().BeNull(
            "no endpoint is generated for a property the filter cannot be written against");
    }

    /// <summary>The control: the same entity with the attribute on a string is accepted.</summary>
    [Fact]
    public void Autocomplete_OnAStringProperty_DoesNotReportPrag0550()
    {
        var source = """
            using Pragmatic.Endpoints.Attributes;

            namespace TestApp.Entities
            {
                public class Product
                {
                    public int Id { get; set; }
                    [Autocomplete] public string Code { get; set; } = "";
                }
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0550").Should().BeFalse();
        GetGeneratedSource(result, "Autocomplete").Should().NotBeNull();
    }
}
