using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     <c>PendingContractGenerator</c> marks an endpoint as not-yet-implemented when its body is
///     <c>throw Behavior.Pending()</c>, and the contract-test generator then skips it. Recognising the
///     call by text ("the receiver's name ends with Behavior") meant any unrelated
///     <c>FooBehavior.Pending()</c> made a fully implemented endpoint's contract test vanish.
/// </summary>
public class PendingContractGeneratorTests
{
    private const string Stubs = """
        namespace Pragmatic.Authoring
        {
            public sealed class PendingBehaviorException : System.Exception
            {
                public PendingBehaviorException(string? note) { }
            }

            public static class Behavior
            {
                public static PendingBehaviorException Pending(string? note = null) => new(note);
            }
        }

        namespace Pragmatic.Endpoints.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EndpointAttribute : System.Attribute { }
        }
        """;

    private static string? Run(string body)
    {
        var result = GeneratorTestHelper.RunGenerator<PendingContractGenerator>(Stubs + "\n" + body);
        return GeneratorTestHelper.GetGeneratedSource(result, "PendingContracts");
    }

    [Fact]
    public void RealBehaviorPending_ExpressionBody_IsReportedAsPending()
    {
        var generated = Run("""
            namespace MyApp
            {
                [Pragmatic.Endpoints.Attributes.Endpoint]
                public partial class GetOrder
                {
                    public string Handle() => throw global::Pragmatic.Authoring.Behavior.Pending();
                }
            }
            """);

        generated.Should().NotBeNull();
        generated.Should().Contain("PendingContract(\"MyApp.GetOrder\")");
    }

    [Fact]
    public void RealBehaviorPending_StatementBody_IsReportedAsPending()
    {
        var generated = Run("""
            namespace MyApp
            {
                using Pragmatic.Authoring;

                [Pragmatic.Endpoints.Attributes.Endpoint]
                public partial class GetOrder
                {
                    public string Handle() { throw Behavior.Pending("ORD-1"); }
                }
            }
            """);

        generated.Should().NotBeNull();
        generated.Should().Contain("PendingContract(\"MyApp.GetOrder\")");
    }

    // The defect: a user type whose name merely ends with "Behavior" produced a false PendingContract,
    // silently removing the endpoint's contract test.
    [Fact]
    public void UnrelatedTypeNamedFooBehavior_IsNotReportedAsPending()
    {
        var generated = Run("""
            namespace MyApp
            {
                public static class RetryBehavior
                {
                    public static System.Exception Pending(string? note = null) => new System.Exception(note);
                }

                [Pragmatic.Endpoints.Attributes.Endpoint]
                public partial class GetOrder
                {
                    public string Handle() => throw RetryBehavior.Pending();
                }
            }
            """);

        generated.Should().BeNull(
            "RetryBehavior.Pending() is the user's own code and says nothing about the endpoint");
    }

    [Fact]
    public void ImplementedEndpoint_IsNotReportedAsPending()
    {
        var generated = Run("""
            namespace MyApp
            {
                [Pragmatic.Endpoints.Attributes.Endpoint]
                public partial class GetOrder
                {
                    public string Handle() => "done";
                }
            }
            """);

        generated.Should().BeNull();
    }
}
