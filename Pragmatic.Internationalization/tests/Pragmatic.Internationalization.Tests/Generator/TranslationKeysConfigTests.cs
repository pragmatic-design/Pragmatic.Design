using System.Collections.Immutable;
using System.Reflection;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     Regression tests for finding #1 (2026-07): the [TranslationKeys(...)] attribute is honored.
///     Before the FQN fix, ForAttributeWithMetadataName never matched (wrong namespace) and every
///     configured option was silently ignored — the generator always fell back to defaults.
/// </summary>
public class TranslationKeysConfigTests : I18NGeneratorTestBase
{
    [Fact]
    public void CustomClassName_IsHonored()
    {
        var hints = RunWithAssemblyAttribute(
            "[assembly: TranslationKeys(ClassName = \"Keys\")]",
            ("translations/en.json", "{\"welcome\":\"Hello\"}"));

        hints.Should().Contain(h => h.Contains("Keys"),
            $"ClassName=Keys must drive the generated type name; got: [{string.Join(", ", hints)}]");
        hints.Should().NotContain(h => h.EndsWith(".T.g.cs"),
            "the default 'T' name must not be used when ClassName is set");
    }

    [Fact]
    public void CustomNamespace_IsHonored()
    {
        var hints = RunWithAssemblyAttribute(
            "[assembly: TranslationKeys(Namespace = \"My.Custom.Ns\")]",
            ("translations/en.json", "{\"welcome\":\"Hello\"}"));

        hints.Should().Contain(h => h.Contains("My.Custom.Ns"),
            $"Namespace must be honored; got: [{string.Join(", ", hints)}]");
    }

    [Fact]
    public void NoAttribute_FallsBackToDefaultT()
    {
        var hints = RunWithAssemblyAttribute(
            attributeSource: null,
            ("translations/en.json", "{\"welcome\":\"Hello\"}"));

        hints.Should().Contain(h => h.EndsWith(".T.g.cs") || h.EndsWith("T.g.cs"),
            $"without the attribute the default 'T' name applies; got: [{string.Join(", ", hints)}]");
    }

    private static List<string> RunWithAssemblyAttribute(
        string? attributeSource,
        params (string Path, string Content)[] files)
    {
        var source = attributeSource is null
            ? ""
            : $"using Pragmatic.Internationalization.Attributes;\n{attributeSource}";

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            GeneratorTestHelper.FromTypeAssembly(typeof(Internationalization.Types.CultureCode))
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the test source must compile for the attribute to bind");

        var additionalTexts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))
            .ToImmutableArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new PragmaticSourceGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToList();
    }
}
