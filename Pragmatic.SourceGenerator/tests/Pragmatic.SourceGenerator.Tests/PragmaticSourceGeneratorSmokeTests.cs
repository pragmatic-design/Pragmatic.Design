using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests;

/// <summary>
/// Smoke tests for the unified source generator.
/// Verifies the generator can be instantiated and run without errors.
/// Feature-specific tests live in Features/{FeatureName}/
/// </summary>
public class PragmaticSourceGeneratorSmokeTests
{
    [Fact]
    public void Generator_EmptySource_ProducesNoOutput()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>("// empty", []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
        result.HasGeneratedFiles.Should().BeFalse();
    }
}
