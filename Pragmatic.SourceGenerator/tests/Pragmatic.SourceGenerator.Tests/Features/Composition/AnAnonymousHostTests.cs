using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A host that deliberately has no authentication says so with <c>[AnonymousHost]</c>, and the
///     generator knows it at compile time: no root <c>RequireAuthorization()</c>, no PRAG1695.
/// </summary>
/// <remarks>
///     Before the attribute the only way to say it was two statements of one fact — an option read at
///     runtime, which the compile-time check could not see, and a <c>NoWarn</c> to silence the check.
///     Without the declaration the host it describes answers 401 on every endpoint, so PRAG1695 is an
///     error.
/// </remarks>
public class AnAnonymousHostTests
{
    [Fact]
    public void AnAnonymousHost_HasNoRootAuthorization()
    {
        var mapping = HostWithOneModuleEndpoint.MapAllEndpointsOf(HostWithOneModuleEndpoint.Run(anonymous: true));

        mapping.Should().NotContain("root.RequireAuthorization()");
        mapping.Should().Contain("root.RequireRateLimiting(",
            "only the authorization default is dropped; the rest of the root options still apply");
    }

    [Fact]
    public void AnAnonymousHost_IsNotToldItLacksIdentity()
    {
        var result = HostWithOneModuleEndpoint.Run(anonymous: true);

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG1695").Should().BeEmpty();
    }

    /// <summary>The control: the same host, undeclared, requires authorization it cannot provide.</summary>
    [Fact]
    public void AHostWithoutTheDeclaration_RequiresAuthorization_AndIsRefused()
    {
        var result = HostWithOneModuleEndpoint.Run(anonymous: false);

        HostWithOneModuleEndpoint.MapAllEndpointsOf(result).Should().Contain("root.RequireAuthorization();");

        var diagnostic = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG1695").Should().ContainSingle().Which;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("[AnonymousHost]");
        diagnostic.GetMessage().Should().NotContain("NoWarn");
    }
}
