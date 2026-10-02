using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A module has to be able to say that a route belongs to no tenant.
/// </summary>
/// <remarks>
///     <para>
///         The metadata that exempts a route from tenant resolution existed — <c>TenantAgnosticEndpoint</c>
///         — and was attached from exactly one place inside the framework. <b>No attribute declared
///         it</b>, so a module's operation could not ask for it: an <c>[AllowAnonymous]</c> liveness
///         probe was refused with <b>400</b> in a multi-tenant host, before the route ran, because the
///         default is <c>RequireTenant</c>.
///     </para>
///     <para>
///         ⚠️ <c>[AllowAnonymous]</c> is not the same declaration and never was. It lifts
///         authentication; the tenant refusal happens at order 92, independently of who is asking.
///         The framework's own aggregated <c>/health</c> worked, which is what made the mechanism
///         look reachable.
///     </para>
/// </remarks>
public class ARouteThatBelongsToNoTenantTests : EndpointsGeneratorTestBase
{
    private const string Marker =
        "WithMetadata(global::Pragmatic.MultiTenancy.TenantAgnosticEndpoint.Instance)";

    private static string Source(string attributes) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Ops;

        [DomainAction]
        [Endpoint(HttpVerb.Get, "api/status")]
        {{attributes}}
        public partial class StatusAction : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("ok"));
        }
        """;

    private static string TheEndpoint(string attributes)
    {
        var generated = GetGeneratedSource(RunGenerator(Source(attributes)), "StatusAction.Endpoint");

        generated.Should().NotBeNull("the endpoint is generated at all");

        return generated!;
    }

    /// <summary>The declaration reaches the route.</summary>
    [Fact]
    public void ADeclaredTenantAgnosticRoute_CarriesTheMarker()
    {
        TheEndpoint("[AllowAnonymous]\n[TenantAgnostic]").Should().Contain(Marker,
            "the tenant middleware reads this metadata, and nothing else exempts a route");
    }

    /// <summary>
    ///     The control: <c>[AllowAnonymous]</c> alone does not exempt it, which is the defect.
    /// </summary>
    /// <remarks>
    ///     Without it, "the marker is emitted" would be satisfied by emitting it for every anonymous
    ///     route — which would silently drop the tenant requirement from every public endpoint in a
    ///     multi-tenant application, the opposite mistake and a worse one.
    /// </remarks>
    [Fact]
    public void AnonymousAlone_DoesNotCarryTheMarker()
    {
        TheEndpoint("[AllowAnonymous]").Should().NotContain(Marker,
            "lifting authentication is a different declaration from belonging to no tenant");
    }

    /// <summary>
    ///     And the declaration counts on its own — an authenticated route may still belong to no tenant.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Found while fixing the first case: the model that carries route-level declarations was
    ///     built only when there were permissions, an anonymous marker or a policy, so an operation
    ///     declaring <em>only</em> <c>[TenantAgnostic]</c> had it read and then dropped. That is the
    ///     same defect one level down from the one with no attribute at all.
    /// </remarks>
    [Fact]
    public void TenantAgnosticAlone_CarriesTheMarker()
    {
        TheEndpoint("[TenantAgnostic]").Should().Contain(Marker,
            "a route can belong to no tenant and still require a user");
    }

    /// <summary>The second control: a route that declares neither carries neither.</summary>
    [Fact]
    public void ARouteThatDeclaresNothing_CarriesNeither()
    {
        var generated = TheEndpoint("");

        generated.Should().NotContain(Marker);
        generated.Should().NotContain("AllowAnonymous()");
    }
}
