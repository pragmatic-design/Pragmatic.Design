using System;
using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     <c>[UseCase]</c> and <c>[Rule]</c> are read, and what reads them is the generated
///     use-case catalog their own summaries promised.
/// </summary>
/// <remarks>
///     <para>
///         Both attributes are public, documented with a copyable <c>&lt;example&gt;</c>, and name their
///         own consumer — the generated use-case catalog. Both are inert at runtime, so a reader who
///         notices nothing happens concludes it is working as documented: a missing catalog would stay
///         invisible.
///     </para>
///     <para>
///         "Declared and never read" means the implementation is missing, not that the declaration
///         should go — hence the catalog. These tests run the full <see cref="PragmaticSourceGenerator" /> over source that writes the
///         attributes, because a template fed a hand-built model proves nothing about whether the
///         probe matches what an author actually writes.
///     </para>
/// </remarks>
public class TheUseCaseCatalogIsGeneratedFromTheAttributesTests
{
    private const string CatalogFile = "_Infra.UseCases.Generated.g.cs";

    /// <summary>
    ///     The authoring surface as <c>Pragmatic.Abstractions</c> declares it, so the test compiles
    ///     without a package reference — the shape, not a copy of the doc comments.
    /// </summary>
    private const string AuthoringShim = """
        namespace Pragmatic.Authoring
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method,
                AllowMultiple = true, Inherited = false)]
            public sealed class RuleAttribute : System.Attribute
            {
                public RuleAttribute(string text) { Text = text; }
                public string Text { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method,
                AllowMultiple = false, Inherited = false)]
            public sealed class UseCaseAttribute : System.Attribute
            {
                public UseCaseAttribute(string id) { Id = id; }
                public string Id { get; }
                public string? Title { get; init; }
            }

            public sealed class UseCaseDescriptor
            {
                public string? Id { get; init; }
                public string? Title { get; init; }
                public string Target { get; init; } = "";
                public string File { get; init; } = "";
                public int Line { get; init; }
                public System.Collections.Generic.IReadOnlyList<string> Rules { get; init; } = [];
            }
        }
        """;

    [Fact]
    public void AUseCaseAndItsRules_ReachTheCatalogWithTheAuthorsWords()
    {
        var source = AuthoringShim + """

            namespace Pharmacy.Dispensing
            {
                [Pragmatic.Authoring.UseCase("DRG-DISPENSE", Title = "Dispense a drug")]
                [Pragmatic.Authoring.Rule("Quantity to dispense must be positive")]
                [Pragmatic.Authoring.Rule("Stock on hand cannot go below zero")]
                public sealed class DispenseDrugMutation { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey(CatalogFile);

        var catalog = generated[CatalogFile];

        catalog.Should().Contain("Id = \"DRG-DISPENSE\"");
        catalog.Should().Contain("Title = \"Dispense a drug\"");
        catalog.Should().Contain("Target = \"Pharmacy.Dispensing.DispenseDrugMutation\"",
            "the operation has to be nameable from the catalog, which a simple name is not");
        catalog.Should().Contain("File = \"TestSource.cs\"");
        catalog.Should().Contain("\"Quantity to dispense must be positive\"");
        catalog.Should().Contain("\"Stock on hand cannot go below zero\"",
            "every rule, not the first — [Rule] is AllowMultiple and the shape invites a list");

        LineOf(catalog).Should().BeGreaterThan(0, "a line of 0 would say the location was never read");
    }

    /// <summary>
    ///     The control that keeps "a catalog is generated" from being satisfied by always generating
    ///     one: a module that writes neither attribute gets no file at all.
    /// </summary>
    [Fact]
    public void WithNeitherAttribute_NoCatalogIsGenerated()
    {
        var source = AuthoringShim + """

            namespace Pharmacy.Dispensing
            {
                public sealed class DispenseDrugMutation { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Should().NotContainKey(CatalogFile,
            "an empty catalog in every assembly is noise, and would make the assertion above vacuous");
    }

    /// <summary>
    ///     <c>[UseCase]</c> targets methods too, and a catalog that only walked types would drop them
    ///     in silence — which is the shape this whole epic is about.
    /// </summary>
    [Fact]
    public void OnAMethod_TheUseCaseIsCatalogedAgainstTheMethod()
    {
        var source = AuthoringShim + """

            namespace Pharmacy.Dispensing
            {
                public sealed class Dispenser
                {
                    [Pragmatic.Authoring.UseCase("DRG-RETURN", Title = "Return unused stock")]
                    [Pragmatic.Authoring.Rule("A return cannot exceed what was dispensed")]
                    public void Return(int quantity) { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Should().ContainKey(CatalogFile);
        generated[CatalogFile].Should().Contain("Target = \"Pharmacy.Dispensing.Dispenser.Return\"");
        generated[CatalogFile].Should().Contain("\"A return cannot exceed what was dispensed\"");
    }

    /// <summary>
    ///     ⚠️ A <c>[Rule]</c> with no <c>[UseCase]</c> beside it is still living specification, and
    ///     dropping it is exactly how <c>[Rule]</c> came to be unread in the first place.
    /// </summary>
    /// <remarks>
    ///     It is kept in its own list rather than in <c>All</c>, so the catalog's own name stays true:
    ///     an entry there has a use-case identifier because every one of them carries one.
    /// </remarks>
    [Fact]
    public void ARuleWithNoUseCase_IsKeptInItsOwnListRatherThanDropped()
    {
        var source = AuthoringShim + """

            namespace Pharmacy.Dispensing
            {
                [Pragmatic.Authoring.UseCase("DRG-DISPENSE")]
                public sealed class DispenseDrugMutation { }

                [Pragmatic.Authoring.Rule("A recall removes the batch from every shelf")]
                public sealed class RecallBatchAction { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source);

        var catalog = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)[CatalogFile];

        catalog.Should().Contain("RulesWithoutAUseCase");
        catalog.Should().Contain("\"A recall removes the batch from every shelf\"");

        var all = Between(catalog, "All { get; } =", "RulesWithoutAUseCase");
        all.Should().NotContain("RecallBatchAction",
            "All is the use-case catalog: an entry without an identifier there would make its name a lie");
        all.Should().Contain("DispenseDrugMutation");
    }

    /// <summary>
    ///     The document half of the promise: the catalog is also Markdown a human reads, and it cannot
    ///     drift from the code because the same compilation writes it.
    /// </summary>
    [Fact]
    public void TheCatalogIsAlsoMarkdown_CarryingTheIdTitleAndRules()
    {
        var source = AuthoringShim + """

            namespace Pharmacy.Dispensing
            {
                [Pragmatic.Authoring.UseCase("DRG-DISPENSE", Title = "Dispense a drug")]
                [Pragmatic.Authoring.Rule("Quantity to dispense must be positive")]
                public sealed class DispenseDrugMutation { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source);

        var markdown = VerbatimConstant(
            GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)[CatalogFile]);

        markdown.Should().StartWith("# Use cases");
        markdown.Should().Contain("DRG-DISPENSE");
        markdown.Should().Contain("Dispense a drug");
        markdown.Should().Contain("Quantity to dispense must be positive");
    }

    /// <summary>The first <c>Line = …</c> the catalog emits.</summary>
    private static int LineOf(string catalog)
    {
        var match = System.Text.RegularExpressions.Regex.Match(catalog, @"Line = (\d+)");
        match.Success.Should().BeTrue("the catalog has to say where the declaration is");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The slice of <paramref name="text" /> between two markers.</summary>
    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);
        return text.Substring(start, end - start);
    }

    /// <summary>The payload of the generated <c>@"…"</c> literal, with its doubling undone.</summary>
    private static string VerbatimConstant(string generatedSource)
    {
        var start = generatedSource.IndexOf("@\"", StringComparison.Ordinal);
        var end = generatedSource.LastIndexOf("\";", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the Markdown travels in a verbatim literal");
        end.Should().BeGreaterThan(start);
        return generatedSource.Substring(start + 2, end - start - 2).Replace("\"\"", "\"");
    }
}
