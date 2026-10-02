using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests;

/// <summary>
/// Verifies <c>FeatureDetector</c> gating: a feature only runs when its exact marker
/// type (by fully-qualified metadata name) is present. A look-alike attribute in a
/// foreign namespace must NOT trigger generation. These run the full unified generator.
/// </summary>
public class FeatureDetectorGatingTests
{
    [Fact]
    public void Generator_MappingAttributeInWrongNamespace_ProducesNoMappingOutput()
    {
        // Detector probes "Pragmatic.Mapping.Attributes.MapFromAttribute`1". A same-named
        // generic attribute under a different namespace must not enable Mapping.
        var source = """
            namespace NotPragmatic.Mapping.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class MapFromAttribute<TSource> : System.Attribute { }
            }

            namespace TestApp
            {
                public class User { public string Name { get; set; } }

                [NotPragmatic.Mapping.Attributes.MapFrom<User>]
                public partial class UserDto { public string Name { get; set; } }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Keys.Should().NotContain(k => k.Contains("Mapping"));
    }

    [Fact]
    public void Generator_ConfigurationAttributeInWrongNamespace_ProducesNoOutput()
    {
        // Detector probes "Pragmatic.Configuration.ConfigurationAttribute".
        var source = """
            namespace Other.Configuration
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class ConfigurationAttribute : System.Attribute { }
            }

            namespace TestApp
            {
                [Other.Configuration.Configuration]
                public partial class SmtpOptions { public string Host { get; set; } }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        result.HasGeneratedFiles.Should().BeFalse();
    }

    [Fact]
    public void Generator_FastEnumMarkerPresent_EnablesFastEnumGeneration()
    {
        // FastEnum is keyed off "Pragmatic.FastEnumAttribute". Presence of the marker
        // enables FastEnum output; absence of the Mapping marker keeps Mapping off.
        var source = """
            namespace Pragmatic
            {
                [System.AttributeUsage(System.AttributeTargets.Enum)]
                public sealed class FastEnumAttribute : System.Attribute { }
            }

            namespace TestApp
            {
                [Pragmatic.FastEnum]
                public enum Priority { Low, High }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Keys.Should().Contain(k => k == "TestApp.Priority.FastEnum.g.cs");
        generated.Keys.Should().NotContain(k => k.Contains("Mapping"));
    }

    [Fact]
    public void Generator_NoMarkerTypes_ProducesNoOutput()
    {
        // No Pragmatic marker types at all → every feature is gated off.
        var source = """
            namespace TestApp
            {
                public class PlainClass { public int Value { get; set; } }
                public enum PlainEnum { A, B }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
        result.HasGeneratedFiles.Should().BeFalse();
    }
}
