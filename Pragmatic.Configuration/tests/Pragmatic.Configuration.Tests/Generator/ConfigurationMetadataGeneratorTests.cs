using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
///     Tests for [PragmaticMetadata(MetadataCategory.Configuration, ...)] generation
///     when Composition is referenced.
/// </summary>
public class ConfigurationMetadataGeneratorTests : ConfigurationGeneratorTestBase
{
    [Fact]
    public void WithComposition_GeneratesMetadataAttribute()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration]
            public partial class BookingOptions
            {
                public string HotelName { get; set; } = "";
                public int MaxGuests { get; set; } = 10;
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("PragmaticMetadata");
        generated.Should().Contain("MetadataCategory.Configuration");
        generated.Should().Contain("Pragmatic.Configuration.SourceGenerator");
    }

    [Fact]
    public void WithoutComposition_DoesNotGenerateMetadata()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration]
            public partial class SimpleOptions
            {
                public int Value { get; set; } = 42;
            }
            """;

        var result = RunGenerator(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().BeNull("metadata should not be generated without Composition reference");
    }

    [Fact]
    public void Metadata_ContainsConfigurationDetails()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration(SectionPath = "Services:Booking")]
            public partial class BookingOptions
            {
                public string HotelName { get; set; } = "";
                public int MaxGuests { get; set; } = 10;
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("configurationsCount");
        generated.Should().Contain("BookingOptions");
        generated.Should().Contain("Services:Booking");
    }

    [Fact]
    public void Metadata_ContainsPropertyInfo()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration]
            public partial class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
                public int TimeoutMs { get; set; } = 5000;
                public bool EnableRetry { get; set; } = true;
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("ConnectionString");
        generated.Should().Contain("TimeoutMs");
        generated.Should().Contain("EnableRetry");
    }

    [Fact]
    public void Metadata_ContainsValidationRules()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp.Config;

            [Configuration]
            public partial class ValidatedOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                [Range(1, 100)]
                public int MaxRetries { get; set; } = 3;

                [MaxLength(256)]
                public string Endpoint { get; set; } = "";
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("validation");
        generated.Should().Contain("required");
        generated.Should().Contain("range");
        generated.Should().Contain("maxLength");
    }

    [Fact]
    public void Metadata_MultipleConfigurations_CountsCorrectly()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration]
            public partial class BookingOptions
            {
                public int MaxGuests { get; set; } = 10;
            }

            [Configuration(SectionPath = "Services:Payment")]
            public partial class PaymentOptions
            {
                public int TimeoutMs { get; set; } = 5000;
            }

            [Configuration]
            public partial class CacheOptions
            {
                public int TtlSeconds { get; set; } = 300;
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("BookingOptions");
        generated.Should().Contain("PaymentOptions");
        generated.Should().Contain("CacheOptions");
    }

    [Fact]
    public void Metadata_SkipsNonPartialClasses()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration]
            public class NonPartialOptions
            {
                public int Value { get; set; } = 42;
            }

            [Configuration]
            public partial class ValidOptions
            {
                public int Timeout { get; set; } = 30;
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("ValidOptions");
        generated.Should().NotContain("NonPartialOptions");
    }

    [Fact]
    public void Metadata_ContainsValidateOnStartFlag()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace TestApp.Config;

            [Configuration(ValidateOnStart = false)]
            public partial class LazyOptions
            {
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("validateOnStart");
    }

    [Fact]
    public void Metadata_ContainsNamespaceInfo()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Settings.Database;

            [Configuration]
            public partial class ConnectionOptions
            {
                public string Host { get; set; } = "localhost";
            }
            """;

        var result = RunGeneratorWithComposition(source);

        var generated = GetGeneratedSource(result, "_Metadata.Configuration");
        generated.Should().NotBeNull();
        generated.Should().Contain("MyApp.Settings.Database");
    }
}
