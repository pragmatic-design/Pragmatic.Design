using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Temporal;

/// <summary>
///     End-to-end: the unified generator discovers timezone conversion attributes on DTO
///     properties and emits the per-assembly TemporalJsonBehaviorRegistry registration.
///     Marker types are stubbed so FeatureDetector triggers without the runtime packages.
/// </summary>
public class TemporalBehaviorsGeneratorTests
{
    private const string Stubs = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }
        }
        namespace Pragmatic.Temporal.Json.Behaviors
        {
            public enum TemporalJsonBehavior
            {
                AsUtc, FromClientTimezone, FromBusinessTimezone,
                ToClientTimezone, ToBusinessTimezone, KeepTimezone
            }

            public static class TemporalJsonBehaviorRegistry
            {
                public static void Register(System.Type dtoType, string propertyName, TemporalJsonBehavior behavior) { }
            }
        }
        namespace Pragmatic.Temporal.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class AsUtcAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class FromClientTimezoneAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class FromBusinessTimezoneAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class ToClientTimezoneAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class ToBusinessTimezoneAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
            public sealed class KeepTimezoneAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void AnnotatedProperties_EmitBehaviorsRegistration()
    {
        var source = Stubs + """

            namespace App.Orders.Dtos
            {
                using Pragmatic.Temporal.Attributes;

                public class OrderResponse
                {
                    [ToClientTimezone]
                    public System.DateTimeOffset CreatedAt { get; set; }

                    [AsUtc]
                    public System.DateTime? ProcessedAt { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        var behaviors = generated
            .Where(kv => kv.Key.Contains("Temporal.Behaviors"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        behaviors.Should().NotBeNull("annotated DTO properties should produce the behaviors registration");
        behaviors!.Should()
            .Contain("AddAppTemporalBehaviors")
            .And.Contain(
                "TemporalJsonBehaviorRegistry.Register(typeof(global::App.Orders.Dtos.OrderResponse), \"CreatedAt\", global::Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehavior.ToClientTimezone);")
            .And.Contain(
                "TemporalJsonBehaviorRegistry.Register(typeof(global::App.Orders.Dtos.OrderResponse), \"ProcessedAt\", global::Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehavior.AsUtc);")
            .And.Contain("return services;");

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
    }

    [Fact]
    public void UnsupportedPropertyType_ReportsPrag0905_AndSkipsRegistration()
    {
        var source = Stubs + """

            namespace App.Dtos
            {
                using Pragmatic.Temporal.Attributes;

                public class BadDto
                {
                    [ToClientTimezone]
                    public string Label { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0905").Should().BeTrue();

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Keys.Should().NotContain(k => k.Contains("Temporal.Behaviors"),
            "a lone unsupported property must not produce a registration file");
    }

    [Fact]
    public void MixedValidAndInvalid_RegistersOnlyValid()
    {
        var source = Stubs + """

            namespace App.Dtos
            {
                using Pragmatic.Temporal.Attributes;

                public class MixedDto
                {
                    [AsUtc]
                    public System.DateTimeOffset At { get; set; }

                    [FromClientTimezone]
                    public int NotADate { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0905").Should().BeTrue();

        var behaviors = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Temporal.Behaviors"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

        behaviors.Should().NotBeNull();
        behaviors!.Should().Contain("\"At\"").And.NotContain("NotADate");
    }

    [Fact]
    public void NoAnnotations_ProducesNoOutput()
    {
        var source = Stubs + """

            namespace App.Dtos
            {
                public class PlainDto
                {
                    public System.DateTimeOffset CreatedAt { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys
            .Should().NotContain(k => k.Contains("Temporal.Behaviors"));
    }

    [Fact]
    public void RegistryTypeAbsent_GatesOffGeneration()
    {
        // Attributes exist but Pragmatic.Temporal.Json (the registry) is not referenced:
        // HasTemporalJson is false, so the feature must stay silent.
        var source = """
            namespace Pragmatic.Temporal.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Parameter)]
                public sealed class ToClientTimezoneAttribute : System.Attribute { }
            }
            namespace App.Dtos
            {
                public class Dto
                {
                    [Pragmatic.Temporal.Attributes.ToClientTimezone]
                    public System.DateTimeOffset CreatedAt { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys
            .Should().NotContain(k => k.Contains("Temporal.Behaviors"));
    }

    [Fact]
    public void LookAlikeAttributeInForeignNamespace_ProducesNoOutput()
    {
        // FeatureDetector probes the registry type; here it exists, but the attribute FQN differs.
        var source = Stubs + """

            namespace NotPragmatic.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Property)]
                public sealed class ToClientTimezoneAttribute : System.Attribute { }
            }
            namespace App.Dtos
            {
                public class Dto
                {
                    [NotPragmatic.Attributes.ToClientTimezone]
                    public System.DateTimeOffset CreatedAt { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys
            .Should().NotContain(k => k.Contains("Temporal.Behaviors"));
    }
}
