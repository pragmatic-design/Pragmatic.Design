using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A permission the auto-derivation switch produced is on no attribute, so the manifest — which is
///     built from endpoint symbols — could not see it. Actions contributes it, labelled with how it got
///     there, and the label is only written when it is something other than the historical
///     <c>"endpoint"</c>: writing that one would rewrite the manifest of every assembly that never turns
///     the switch on.
/// </summary>
public class DerivedPermissionManifestTests : EndpointsGeneratorTestBase
{
    private const string ActionEndpoint = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        {{OPT_IN}}

        namespace Showcase.Billing.Refunds;

        [Boundary]
        public partial class BillingBoundary;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "/refunds")]
        public partial class IssueRefundAction : DomainAction<Guid>
        {
            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.NewGuid()));
        }
        """;

    private static string Source(bool optIn)
        => ActionEndpoint.Replace("{{OPT_IN}}",
            optIn ? "[assembly: Pragmatic.Authorization.PragmaticAutoDerivePermissions]" : "");

    [Fact]
    public void FlagOn_DerivedPermission_IsListedInTheManifestWithItsSource()
    {
        var result = RunGenerator(Source(optIn: true));

        var manifest = GetGeneratedSource(result, "PragmaticManifest");
        manifest.Should().NotBeNull();
        manifest!.Should().Contain("\"name\":\"billing.issue-refund\"")
            .And.Contain("\"source\":\"auto-derived\"");
    }

    [Fact]
    public void FlagOff_ManifestListsNoDerivedPermissionAndNoSourceLabel()
    {
        var result = RunGenerator(Source(optIn: false));

        var manifest = GetGeneratedSource(result, "PragmaticManifest");
        manifest.Should().NotBeNull();
        manifest!.Should().NotContain("billing.issue-refund")
            .And.NotContain("\"source\"");
    }
}
