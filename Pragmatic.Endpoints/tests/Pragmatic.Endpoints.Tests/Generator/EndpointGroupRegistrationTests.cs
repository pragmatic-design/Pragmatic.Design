using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Verifies that endpoints assigned to an <c>[EndpointGroup]</c> via
///     <c>[Endpoint(...)]</c> are registered under a
///     [EndpointGroup&lt;TGroup&gt;]
///     <c>MapGroup</c> with the group route prefix, and that endpoint-level
///     <c>[RequirePermission]</c> still wires authorization on the grouped endpoint.
/// </summary>
public class EndpointGroupRegistrationTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void GroupedEndpoint_GeneratesMapGroupWithRoutePrefix()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Admin;

            [EndpointGroup("/api/v1/admin", Tag = "Admin")]
            public sealed class AdminGroup { }

            public record AdminResponse(string Data);

            [Endpoint(HttpVerb.Get, "/settings")]
            [EndpointGroup<AdminGroup>]
            public partial class AdminSettingsEndpoint : Endpoint<AdminResponse>
            {
                public override Task<Result<AdminResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<AdminResponse>>(new AdminResponse("ok"));
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.First(s => s.Key.Contains("Endpoints.Registration")).Value;

        registration.Should().Contain("MapGroup(\"/api/v1/admin\")");
        registration.Should().Contain("AdminSettingsEndpoint.MapEndpoint");
    }

    [Fact]
    public void GroupedEndpoint_WithRequirePermission_GeneratesAuthorizationOnHandler()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Admin;

            [EndpointGroup("/api/v1/admin")]
            public sealed class AdminGroup { }

            public record AdminResponse(string Data);

            [Endpoint(HttpVerb.Get, "/secured")]
            [EndpointGroup<AdminGroup>]
            [RequirePermission("admin:manage")]
            public partial class SecuredAdminEndpoint : Endpoint<AdminResponse>
            {
                public override Task<Result<AdminResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<AdminResponse>>(new AdminResponse("ok"));
            }
            """;

        var result = RunGenerator(source);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("RequireAuthorization");
        handler.Should().Contain("admin:manage");
    }

    [Fact]
    public void NestedGroup_GeneratesCombinedRoutePrefix()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Nested;

            [EndpointGroup("/api")]
            public sealed class ApiGroup { }

            [EndpointGroup("/orders")]
            [EndpointGroup<ApiGroup>]
            public sealed class OrdersGroup { }

            public record OrderResponse(int Id);

            [Endpoint(HttpVerb.Get, "/list")]
            [EndpointGroup<OrdersGroup>]
            public partial class ListOrdersEndpoint : Endpoint<OrderResponse>
            {
                public override Task<Result<OrderResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderResponse>>(new OrderResponse(1));
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.First(s => s.Key.Contains("Endpoints.Registration")).Value;

        // Nested group prefix combines parent + child.
        registration.Should().Contain("/api/orders");
        registration.Should().Contain("ListOrdersEndpoint.MapEndpoint");
    }

    [Fact]
    public void GroupedEndpoint_RuntimeGroupOptions_AreConsumed()
    {
        // The runtime group block consumes RequiredPermissions/Tags/ResponseCacheDuration/CORS as
        // well as auth + rate-limit policy; left out, they would be inert. Verify they are emitted.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Admin;

            [EndpointGroup("/api/v1/admin", Tag = "Admin")]
            public sealed class AdminGroup { }

            public record AdminResponse(string Data);

            [Endpoint(HttpVerb.Get, "/settings")]
            [EndpointGroup<AdminGroup>]
            public partial class AdminSettingsEndpoint : Endpoint<AdminResponse>
            {
                public override Task<Result<AdminResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<AdminResponse>>(new AdminResponse("ok"));
            }
            """;

        var result = RunGenerator(source);

        var registration = GetGeneratedSourcesAsDictionary(result)
            .First(s => s.Key.Contains("Endpoints.Registration")).Value;

        // RequiredPermissions enforcement (the most serious option to leave inert).
        registration.Should().Contain("RequiredPermissions.Count > 0");
        // Tags, cache duration, CORS.
        registration.Should().Contain("Opts.Tags.Count > 0");
        registration.Should().Contain("Opts.ResponseCacheDuration is int");
        registration.Should().Contain("Opts.EnableCors");
        registration.Should().Contain("RequireCors");
    }

    /// <summary>
    ///     A group's permissions go into the policy as a requirement, like an operation's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not an opaque <c>RequireAssertion</c> comparing the token's claims by hand. That shape
    ///         has two consequences, both measured by this test and its control:
    ///     </para>
    ///     <para>
    ///         ⚠️ Nothing reading endpoint metadata could say what the group required, and
    ///         <c>PragmaticAuthorizationResultHandler</c> would answer a bare <c>ForbiddenError</c>,
    ///         because it finds the permissions on the requirement and there would be none.
    ///     </para>
    ///     <para>
    ///         ⚠️ And the comparison would not ask <c>IPermissionChecker</c>, so roles and wildcards would
    ///         not expand: a role-only token would be refused on a group-gated route where the same token
    ///         passes on an operation-gated one. The requirement's handler asks the checker, which is where that
    ///         expansion lives.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AGroupsPermissions_GoIntoThePolicyAsARequirement()
    {
        var registration = RegistrationOf(GroupedEndpointSource);

        registration.Should().Contain("AddRequirements(",
            "the permissions belong in the metadata, where something can read them");
        registration.Should().Contain("PragmaticPermissionRequirement",
            "the same requirement an operation's [RequirePermission] emits");
        registration.Should().NotContain("RequireAssertion",
            "an opaque assertion is invisible to anything reading the endpoint");
        registration.Should().NotContain("c.Type == \"permission\"",
            "and the claim type is IdentityOptions.PermissionClaimType, not a literal in generated code");
    }

    /// <summary>
    ///     The control: a group whose options carry no permissions gates on nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the permissions become a requirement" would also hold for a generator that
    ///     emitted the requirement unconditionally — and a requirement with an empty permission list
    ///     throws, by its own constructor, rather than granting.
    /// </remarks>
    [Fact]
    public void AGroupWithNoPermissions_GatesOnNothing()
    {
        var registration = RegistrationOf(GroupedEndpointSource);

        var guard = registration.IndexOf("RequiredPermissions.Count > 0", System.StringComparison.Ordinal);
        var requirement = registration.IndexOf("PragmaticPermissionRequirement", System.StringComparison.Ordinal);

        guard.Should().BeGreaterThan(-1, "the emission is guarded by the runtime count");
        requirement.Should().BeGreaterThan(guard, "and the requirement is built inside that guard");
    }

    /// <summary>One grouped endpoint, for the two tests above.</summary>
    private const string GroupedEndpointSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp.Admin;

        [EndpointGroup("/api/v1/admin", Tag = "Admin")]
        public sealed class AdminGroup { }

        public record AdminResponse(string Data);

        [Endpoint(HttpVerb.Get, "/settings")]
        [EndpointGroup<AdminGroup>]
        public partial class AdminSettingsEndpoint : Endpoint<AdminResponse>
        {
            public override Task<Result<AdminResponse>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<AdminResponse>>(new AdminResponse("ok"));
        }
        """;

    /// <summary>
    ///     A group with no prefix of its own still gets its route builder and its runtime
    ///     options.
    /// </summary>
    /// <remarks>
    ///     A group with an empty prefix is the shape an author reaches for to share configuration, and
    ///     the endpoint-groups page recommended it. Only a group with a non-empty prefix got a
    ///     <c>MapGroup</c> and the <c>ConfigureGroup</c> lookup; this one was mapped straight onto the
    ///     root, so <c>ConfigureGroup("Shared", g =&gt; g.RequireAuthorization = true)</c> applied to
    ///     nothing and nothing said so.
    /// </remarks>
    [Fact]
    public void AGroupWithAnEmptyPrefix_GetsItsMapGroupAndItsOptions()
    {
        var registration = RegistrationOf(EmptyPrefixGroupSource);

        registration.Should().Contain("var sharedGroup = root.MapGroup(\"\");",
            "an empty-prefix RouteGroupBuilder is route-neutral and still carries conventions");
        registration.Should().Contain("pragmaticOptions.Groups.TryGetValue(\"Shared\"",
            "the group's ConfigureGroup options are read, prefix or not");
        registration.Should().Contain("SharedStatusEndpoint.MapEndpoint(sharedGroup);",
            "and its endpoints are mapped inside the group, where those options apply");
    }

    /// <summary>The control: an endpoint outside any group stays on the root.</summary>
    [Fact]
    public void AnUngroupedEndpointBesideIt_StaysOnTheRoot()
        => RegistrationOf(EmptyPrefixGroupSource)
            .Should().Contain("PlainStatusEndpoint.MapEndpoint(root);");

    private const string EmptyPrefixGroupSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp.Shared;

        [EndpointGroup("")]
        public sealed class SharedGroup { }

        public record StatusResponse(string Data);

        [Endpoint(HttpVerb.Get, "/shared-status")]
        [EndpointGroup<SharedGroup>]
        public partial class SharedStatusEndpoint : Endpoint<StatusResponse>
        {
            public override Task<Result<StatusResponse>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<StatusResponse>>(new StatusResponse("ok"));
        }

        [Endpoint(HttpVerb.Get, "/plain-status")]
        public partial class PlainStatusEndpoint : Endpoint<StatusResponse>
        {
            public override Task<Result<StatusResponse>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<StatusResponse>>(new StatusResponse("ok"));
        }
        """;

    private static string RegistrationOf(string source)
        => GetGeneratedSourcesAsDictionary(RunGenerator(source))
            .First(s => s.Key.Contains("Endpoints.Registration")).Value;

    [Fact]
    public void Registration_AlwaysRegistersRateLimiter_EvenWithoutCompileTimePolicy()
    {
        // A global DefaultRateLimitPolicy / group RateLimitPolicy / named ConfigureRateLimiter
        // policy is a runtime option. The rate limiter services must be registered regardless of
        // compile-time [RateLimit] presence, otherwise RequireRateLimiting would reference an
        // unregistered policy at runtime. No [RateLimit] anywhere in this source.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Plain;

            public record PlainResponse(string Data);

            [Endpoint(HttpVerb.Get, "/plain")]
            public partial class PlainEndpoint : Endpoint<PlainResponse>
            {
                public override Task<Result<PlainResponse>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<PlainResponse>>(new PlainResponse("ok"));
            }
            """;

        var result = RunGenerator(source);

        var sources = GetGeneratedSourcesAsDictionary(result);
        var registration = sources.First(s => s.Key.Contains("Endpoints.Registration")).Value;

        registration.Should().Contain("services.AddRateLimiter(");
        // The runtime named-policy bridge must be present too.
        registration.Should().Contain("endpointOptions?.RateLimiters");
    }
}
