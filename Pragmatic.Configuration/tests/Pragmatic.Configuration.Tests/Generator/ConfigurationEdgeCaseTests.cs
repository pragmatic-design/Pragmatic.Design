using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
/// Edge case tests for the Configuration source generator.
/// </summary>
public class ConfigurationEdgeCaseTests : ConfigurationGeneratorTestBase
{
    [Fact]
    public void ClassWithNoProperties_GeneratesValidRegistration()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public partial class EmptyOptions
            {
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        // Should still generate valid binding code
        registration.Should().NotBeNull();
        registration.Should().Contain("GetSection(\"Empty\")");
    }

    [Fact]
    public void ReadOnlyProperty_IsExcluded()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                public string ReadWrite { get; set; } = "value";

                // Read-only property — should not be included in binding
                public string ReadOnly => "computed";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        // Should not contain ValidateDataAnnotations since ReadOnly has no setter (filtered out)
        // and ReadWrite has no validation attributes
        registration.Should().NotContain("ValidateDataAnnotations");
    }

    [Fact]
    public void AbstractClass_EmitsPRAG2001()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public abstract partial class AbstractOptions
            {
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2001").Should().BeTrue();
    }

    [Fact]
    public void NonPartialClass_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public class NonPartialOptions
            {
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        // PRAG2000 is the companion analyzer's, on the declaration.
        HasDiagnostic(result, "PRAG2000").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse("nothing was generated into a type that cannot take it");
    }

    [Fact]
    public void RequiredPropertyWithDefaultValue_EmitsPRAG2050()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [Required]
                public string Name { get; set; } = "default";
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2050").Should().BeTrue();
    }

    [Fact]
    public void RequiredPropertyWithoutDefaultValue_DoesNotEmitPRAG2050()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [Required]
                public string Name { get; set; }
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2050").Should().BeFalse();
    }

    [Fact]
    public void Aggregator_ExcludesAbstractConfig_NoCompilationError()
    {
        // A static/abstract [Configuration] gets no RegistrationTemplate (PRAG2001),
        // so the aggregator must not emit a call to its never-generated Add*Options method (CS0103).
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public partial class ValidOptions
            {
                public int Value { get; set; } = 1;
            }

            [Configuration]
            public abstract partial class AbstractOptions
            {
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var aggregator = sources.FirstOrDefault(kv => kv.Key.Contains("ConfigurationExtensions")).Value;

        aggregator.Should().NotBeNull();
        aggregator.Should().Contain("AddValidOptions(");
        aggregator.Should().NotContain("AddAbstractOptions(");
        // The symptom of the bug was a CS0103 (call to a never-generated Add*Options method).
        GetCompilationErrors(result).Should().NotContain(d => d.Id == "CS0103");
    }

    [Theory]
    [InlineData("Options", "Options")]
    [InlineData("AOptions", "A")]
    public void SectionPathInference_BoundaryNames(string className, string expectedSection)
    {
        var source = $$"""
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public partial class {{className}}
            {
                public int Value { get; set; } = 42;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain($"GetSection(\"{expectedSection}\")");
    }

    [Fact]
    public void DeeplyNestedNamespace_GeneratesCorrectly()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyCompany.MyProduct.MyArea.Config;

            [Configuration]
            public partial class DeepOptions
            {
                public string Value { get; set; } = "nested";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("MyCompany.MyProduct.MyArea.Config");
    }

    [Fact]
    public void SingleAggregator_NotGenerated_ForSingleConfig()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp;

            [Configuration]
            public partial class OnlyOptions
            {
                public int Value { get; set; } = 1;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        // Aggregator is always generated (even for 1 config) — it's the registration entry point
        var aggregatorKey = sources.Keys.FirstOrDefault(k => k.Contains("ConfigurationExtensions"));
        aggregatorKey.Should().NotBeNull();
    }
}
