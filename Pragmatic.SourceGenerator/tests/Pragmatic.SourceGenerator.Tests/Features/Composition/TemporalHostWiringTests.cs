using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     Host-mode wiring for the Temporal module: with only the core package referenced the host
///     registers AddPragmaticTemporal(); with the ASP.NET Core integration referenced it upgrades
///     to AddPragmaticTemporalAspNetCore() plus the TemporalContextStep middleware step.
///     Marker types are stubbed so FeatureDetector/CompositionDetector trigger without the
///     runtime packages; the generated host references types absent from the stub compilation,
///     so assertions are on generated text, not compilation success.
/// </summary>
public class TemporalHostWiringTests
{
    private const string HostStubs = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.Temporal.Clock
        {
            public class SystemClock { }
        }
        public static class Program
        {
            public static void Main() { }
        }
        """;

    private const string AspNetCoreStub = """

        namespace Pragmatic.Temporal.AspNetCore.Middleware
        {
            public class TemporalContextMiddleware { }
        }
        """;

    private static string? GetHostServices(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("Host.Services"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

    [Fact]
    public void HostWithTemporalCoreOnly_RegistersAddPragmaticTemporal()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(HostStubs, []);
        var host = GetHostServices(result);

        host.Should().NotBeNull("a host referencing Pragmatic.Temporal must wire the core services");
        host!.Should().Contain("services.AddPragmaticTemporal();")
            .And.NotContain("AddPragmaticTemporalAspNetCore")
            .And.NotContain("TemporalContextStep");
    }

    [Fact]
    public void HostWithTemporalAspNetCore_RegistersWebIntegrationAndMiddlewareStep()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostStubs + AspNetCoreStub, []);
        var host = GetHostServices(result);

        host.Should().NotBeNull();
        host!.Should().Contain("services.AddPragmaticTemporalAspNetCore();")
            .And.Contain("global::Pragmatic.Temporal.AspNetCore.Steps.TemporalContextStep")
            .And.NotContain("services.AddPragmaticTemporal();");
    }

    [Fact]
    public void LibraryWithTemporalAspNetCore_DoesNotEmitHostServices()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            HostStubs + AspNetCoreStub, []);
        GetHostServices(result).Should().BeNull("library-mode compilations must not get host wiring");
    }
}
