using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     The schema-version diagnostics say which version, in which assembly.
/// </summary>
/// <remarks>
///     <para>
///         Two copies of the sentence drift apart. If <c>ValidationError</c> carries a finished sentence
///         while the descriptor carries a format wanting two arguments, and the reporting site passes the
///         sentence as the first of them, the compiler prints <c>"Metadata schema version {0} in {1} is
///         newer than supported"</c>, braces and all. It names neither, and reading the source is the
///         only way to learn which assembly and which version.
///     </para>
///     <para>
///         So there is one copy: the descriptor is the one that words it, and the topology report
///         renders through the same function. These tests are on the pair — a format and its
///         arguments — because testing either alone lets them drift.
///     </para>
/// </remarks>
public class SchemaVersionDiagnosticTests
{
    [Fact]
    public void NewerSchemaVersion_NamesTheVersionAndTheAssembly()
    {
        var message = CompositionDiagnostics.Render("PRAG1611", ["1.9.0", "Acme.Billing"]);

        message.Should().Contain("1.9.0").And.Contain("Acme.Billing");
        message.Should().NotContain("{0}").And.NotContain("{1}");
    }

    [Fact]
    public void IncompatibleSchemaVersion_NamesTheExpectedMajorToo()
    {
        var message = CompositionDiagnostics.Render("PRAG1610", ["2.0.0", "Acme.Billing", "1"]);

        message.Should().Contain("2.0.0").And.Contain("Acme.Billing");
        message.Should().NotContain("{0}").And.NotContain("{1}").And.NotContain("{2}");
    }

    /// <summary>
    ///     Severity comes from the descriptor, not from the shape of the id.
    /// </summary>
    /// <remarks>
    ///     The report read it as <c>EndsWith("10") ? "ERROR" : "WARNING"</c>, which happens to be right
    ///     for the three ids that exist and is wrong for the next one ending in 10.
    /// </remarks>
    [Fact]
    public void SeverityComesFromTheDescriptor()
    {
        CompositionDiagnostics.SchemaSeverity("PRAG1610").Should()
            .Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        CompositionDiagnostics.SchemaSeverity("PRAG1611").Should()
            .Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    /// <summary>An id nothing describes renders as itself rather than throwing.</summary>
    [Fact]
    public void AnUnknownId_RendersAsItself()
    {
        CompositionDiagnostics.Render("PRAG9999", ["a", "b"]).Should().Be("PRAG9999");
    }
}
