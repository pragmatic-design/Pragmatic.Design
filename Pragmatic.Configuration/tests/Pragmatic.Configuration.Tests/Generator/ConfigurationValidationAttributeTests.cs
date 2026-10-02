using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
/// Tests that the generator correctly detects different DataAnnotation validation attributes.
/// </summary>
public class ConfigurationValidationAttributeTests : ConfigurationGeneratorTestBase
{
    [Fact]
    public void MaxLength_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [MaxLength(100)]
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void MinLength_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [MinLength(3)]
                public string Code { get; set; } = "ABC";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void StringLength_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [StringLength(50, MinimumLength = 1)]
                public string Description { get; set; } = "default";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void RegularExpression_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [RegularExpression(@"^[a-z]+$")]
                public string Slug { get; set; } = "default";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void EmailAddress_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [EmailAddress]
                public string AdminEmail { get; set; } = "admin@example.com";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void Url_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [Url]
                public string Endpoint { get; set; } = "https://example.com";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void Phone_IsRecognized_GeneratesValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [Phone]
                public string SupportPhone { get; set; } = "+1234567890";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }

    [Fact]
    public void MultipleAttributes_AllRecognized()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [Configuration]
            public partial class TestOptions
            {
                [Required]
                [StringLength(100, MinimumLength = 1)]
                public string Name { get; set; } = "";

                [Range(1, 9999)]
                public int Port { get; set; } = 8080;

                [Url]
                public string Endpoint { get; set; } = "https://localhost";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(kv => kv.Key.Contains("Registration")).Value;

        registration.Should().NotBeNull();
        registration.Should().Contain("ValidateDataAnnotations");
    }
}
