using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     Two silent misreadings reported as diagnostics: a field the generator cannot classify (PRAG0419;
///     unreported, the field would be left null at runtime) and an empty
///     <c>[ResiliencePolicy("")]</c> name (PRAG0420; unreported, it would reach an invoker that looks up
///     a policy nothing can register).
/// </summary>
public class DependencyAndResilienceDiagnosticsTests
{
    private const string Stubs = """
        namespace Pragmatic.Actions.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DomainActionAttribute : System.Attribute
            {
                public bool Internal { get; set; }
                public bool System { get; set; }
            }
        }

        namespace Pragmatic.Actions.Abstractions
        {
            public abstract class VoidDomainAction { }
        }

        namespace Pragmatic.Resilience.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ResiliencePolicyAttribute : System.Attribute
            {
                public ResiliencePolicyAttribute(string policyName) { PolicyName = policyName; }
                public string PolicyName { get; }
            }
        }

        namespace Pragmatic.Endpoints
        {
            public enum HttpVerb { Get, Post, Put, Patch, Delete }
        }

        namespace Pragmatic.Endpoints.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EndpointAttribute : System.Attribute
            {
                public EndpointAttribute(global::Pragmatic.Endpoints.HttpVerb method, string route) { }
            }
        }

        namespace MyApp
        {
            public interface IPricingService { }
            public sealed class PricingCache { }
        }
        """;

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + "\n" + body);

    [Fact]
    public void UnclassifiableFieldType_ReportsPRAG0419()
    {
        var result = Run("""
            namespace MyApp
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                public partial class RepriceOrder : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private readonly PricingCache _cache = null!;
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeTrue(
            "a concrete field type is not classifiable, and the field is silently not injected");
        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0419")
            .Single().GetMessage().Should().Contain("_cache").And.Contain("PricingCache");
    }

    [Fact]
    public void InterfaceDependency_DoesNotReportPRAG0419()
    {
        var result = Run("""
            namespace MyApp
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                public partial class RepriceOrder : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private readonly IPricingService _pricing = null!;
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeFalse();
    }

    [Fact]
    public void ConstAndStaticFields_DoNotReportPRAG0419()
    {
        var result = Run("""
            namespace MyApp
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                public partial class RepriceOrder : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private const string Prefix = "ORD";
                    private static readonly PricingCache Shared = new();
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0419").Should().BeFalse(
            "state held in a const or static field was never a candidate for injection");
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void BlankResiliencePolicyName_ReportsPRAG0420(string literal)
    {
        var result = Run($$"""
            namespace MyApp
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Resilience.Attributes.ResiliencePolicy({{literal}})]
                public partial class RepriceOrder : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0420").Should().BeTrue();
    }

    // Endpoints read their dependencies through the same detector, so the same field is dropped in the
    // same silence — reported there as PRAG0527.
    [Fact]
    public void UnclassifiableEndpointFieldType_ReportsPRAG0527()
    {
        var result = Run("""
            namespace MyApp
            {
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/prices")]
                public partial class GetPrices : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private readonly PricingCache _cache = null!;
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0527").Should().BeTrue();
        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0527")
            .Single().GetMessage().Should().Contain("_cache").And.Contain("PricingCache");
    }

    [Fact]
    public void InterfaceEndpointDependency_DoesNotReportPRAG0527()
    {
        var result = Run("""
            namespace MyApp
            {
                [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/prices")]
                public partial class GetPrices : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                    private readonly IPricingService _pricing = null!;
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0527").Should().BeFalse();
    }

    [Fact]
    public void NamedResiliencePolicy_DoesNotReportPRAG0420()
    {
        var result = Run("""
            namespace MyApp
            {
                [Pragmatic.Actions.Attributes.DomainAction]
                [Pragmatic.Resilience.Attributes.ResiliencePolicy("payment-provider")]
                public partial class RepriceOrder : Pragmatic.Actions.Abstractions.VoidDomainAction
                {
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0420").Should().BeFalse();
    }
}
