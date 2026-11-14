using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     <c>[NoTenant]</c> reaches the route as the metadata the tenant middleware reads, and is refused beside <c>[TenantAgnostic]</c>
///     (#724): nothing let an operation say it runs in no tenant, so a caller who brought one was served in it.
/// </summary>
public class ARouteThatRunsInNoTenantTests : EndpointsGeneratorTestBase
{
    private const string NoTenantMarker = "WithMetadata(global::Pragmatic.MultiTenancy.NoTenantEndpoint.Instance)";
    private const string AgnosticMarker = "WithMetadata(global::Pragmatic.MultiTenancy.TenantAgnosticEndpoint.Instance)";

    private static string Source(string attributes) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Platform;

        [DomainAction]
        [Endpoint(HttpVerb.Get, "api/platform/tenants")]
        {{attributes}}
        public partial class ListTenantsAction : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("ok"));
        }
        """;

    [Fact]
    public void ADeclaredNoTenantRoute_CarriesTheMarker()
    {
        var result = RunGenerator(Source("[NoTenant]"));
        var generated = GetGeneratedSource(result, "ListTenantsAction.Endpoint");

        generated.Should().NotBeNull();
        generated!.Should().Contain(NoTenantMarker, "the tenant middleware reads this metadata to refuse a tenant");
        generated.Should().NotContain(AgnosticMarker);
        HasDiagnostic(result, "PRAG0556").Should().BeFalse();
    }

    /// <summary>The control: a tenant-agnostic route does not run in no tenant.</summary>
    [Fact]
    public void ATenantAgnosticRoute_DoesNotCarryTheMarker()
    {
        var generated = GetGeneratedSource(RunGenerator(Source("[TenantAgnostic]")), "ListTenantsAction.Endpoint");

        generated!.Should().Contain(AgnosticMarker).And.NotContain(NoTenantMarker);
    }

    [Fact]
    public void BothAttributes_ArePRAG0556()
    {
        var result = RunGenerator(Source("[NoTenant]\n[TenantAgnostic]"));

        GetDiagnosticsById(result, "PRAG0556").Should().ContainSingle()
            .Which.GetMessage().Should().Contain("ListTenantsAction");
    }
}
