using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
/// Snapshot tests for the Configuration source generator.
/// </summary>
public class ConfigurationGeneratorSnapshotTests : ConfigurationGeneratorTestBase
{
    [Fact]
    public Task Registration_SimpleOptions_GeneratesBindingAndValidation()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace MyApp.Config;

            [Configuration]
            public partial class BookingOptions
            {
                [Required]
                public string HotelName { get; set; } = "";

                [Range(1, 100)]
                public int MaxGuests { get; set; } = 10;

                public int CancellationWindowHours { get; set; } = 24;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Registration_ExplicitSectionPath_UsesProvidedPath()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration(SectionPath = "Services:Payment")]
            public partial class PaymentOptions
            {
                public string ApiKey { get; set; } = "";
                public int TimeoutMs { get; set; } = 5000;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Registration_NoValidationAttributes_SkipsValidateDataAnnotations()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class SimpleOptions
            {
                public int Timeout { get; set; } = 30;
                public string Name { get; set; } = "default";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Registration_ValidateOnStartFalse_SkipsValidateOnStart()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace MyApp.Config;

            [Configuration(ValidateOnStart = false)]
            public partial class LazyOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Aggregator_MultipleOptions_GeneratesAggregateMethod()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

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
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var aggregatorKey = sources.Keys.FirstOrDefault(k => k.Contains("ConfigurationExtensions"));
        aggregatorKey.Should().NotBeNull();

        return Verify(sources[aggregatorKey!]);
    }

    [Fact]
    public void Diagnostic_NonPartialClass_IsSkippedWithoutAGeneratorDiagnostic()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public class NonPartialOptions
            {
                public int Value { get; set; } = 42;
            }
            """;

        var result = RunGenerator(source);

        // PRAG2000 is the companion analyzer's, on the declaration.
        HasDiagnostic(result, "PRAG2000").Should().BeFalse();
    }

    [Fact]
    public Task Registration_InferredSectionPath_RemovesOptionsSuffix()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp;

            [Configuration]
            public partial class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Registration_NestedOptions_GeneratesBindingForComplexType()
    {
        var source = """
            using Pragmatic.Configuration;
            using System.ComponentModel.DataAnnotations;

            namespace MyApp.Config;

            public class RetryOptions
            {
                public int MaxRetries { get; set; } = 3;
                public int DelayMs { get; set; } = 1000;
            }

            public class CacheOptions
            {
                public int TtlSeconds { get; set; } = 300;
                public bool Enabled { get; set; } = true;
            }

            [Configuration]
            public partial class ServiceOptions
            {
                [Required]
                public string BaseUrl { get; set; } = "";
                public RetryOptions Retry { get; set; } = new();
                public CacheOptions Cache { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);

        // Compilation errors are expected in test harness (missing Options/Configuration NuGet types)
        // but the generated code itself is correct — verify the registration output
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Registration_GlobalNamespace_GeneratesCorrectly()
    {
        var source = """
            using Pragmatic.Configuration;

            [Configuration]
            public partial class GlobalOptions
            {
                public string Value { get; set; } = "default";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }

    [Fact]
    public Task Sensitive_MarkedProperties_GeneratesKeyClassifier()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration(SectionPath = "Payment")]
            public partial class PaymentOptions
            {
                [Sensitive]
                public string ApiKey { get; set; } = "";

                public int TimeoutMs { get; set; } = 5000;

                [Sensitive]
                public string WebhookSecret { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var classifierKey = sources.Keys.FirstOrDefault(k => k.Contains("SensitiveKeys"));
        classifierKey.Should().NotBeNull("a [Sensitive] property must generate an ISensitiveKeyClassifier");

        return Verify(sources[classifierKey!]);
    }

    [Fact]
    public Task Sensitive_MarkedProperties_AggregatorRegistersClassifier()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class BookingOptions
            {
                [Sensitive]
                public string SigningKey { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var aggregatorKey = sources.Keys.FirstOrDefault(k =>
            k.Contains("ConfigurationExtensions") && !k.Contains("Registration"));
        aggregatorKey.Should().NotBeNull();
        sources[aggregatorKey!].Should().Contain("ISensitiveKeyClassifier",
            "the aggregator must register the generated classifier so stores mask sensitive values");

        return Verify(sources[aggregatorKey!]);
    }

    [Fact]
    public void Sensitive_NoMarkedProperties_DoesNotGenerateClassifier()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class PlainOptions
            {
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        sources.Keys.Should().NotContain(k => k.Contains("SensitiveKeys"),
            "no classifier is emitted when nothing is [Sensitive] — the null default stands");
    }

    [Fact]
    public Task Invariant_MarkedMethod_GeneratesOptionsValidator()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class ScheduleOptions
            {
                public int Start { get; set; }
                public int End { get; set; }

                [ConfigInvariant("End must be greater than Start")]
                public bool EndAfterStart() => End > Start;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var validatorKey = sources.Keys.FirstOrDefault(k => k.Contains("OptionsValidator"));
        validatorKey.Should().NotBeNull("a [ConfigInvariant] method must generate an IValidateOptions<T>");

        return Verify(sources[validatorKey!]);
    }

    [Fact]
    public void Invariant_Registration_RegistersValidator()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class ScheduleOptions
            {
                public int Start { get; set; }
                public int End { get; set; }

                [ConfigInvariant("End must be greater than Start")]
                public bool EndAfterStart() => End > Start;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.First(k => k.Contains("Registration"));
        sources[registrationKey].Should().Contain("IValidateOptions",
            "the registration must wire the generated invariant validator");
    }

    [Fact]
    public void Invariant_None_DoesNotGenerateValidator()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class PlainOptions
            {
                public int Value { get; set; }
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        sources.Keys.Should().NotContain(k => k.Contains("OptionsValidator"));
    }

    [Fact]
    public void Diagnostic_StaticClass_EmitsPRAG2001()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public static partial class StaticOptions
            {
                public static int Value { get; set; } = 42;
            }
            """;

        var result = RunGenerator(source);
        HasDiagnostic(result, "PRAG2001").Should().BeTrue();
    }

    [Fact]
    public Task Registration_ClassNameWithoutOptionsSuffix_UseFullNameAsSection()
    {
        var source = """
            using Pragmatic.Configuration;

            namespace MyApp.Config;

            [Configuration]
            public partial class SmtpSettings
            {
                public string Host { get; set; } = "localhost";
                public int Port { get; set; } = 25;
            }
            """;

        var result = RunGenerator(source);
        var sources = GetGeneratedSourcesAsDictionary(result);

        var registrationKey = sources.Keys.FirstOrDefault(k => k.Contains("Registration"));
        registrationKey.Should().NotBeNull();

        return Verify(sources[registrationKey!]);
    }
}
