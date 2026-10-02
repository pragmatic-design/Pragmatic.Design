using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Verifies that [RequirePermission] / [RequireAnyPermission] put a
///     <c>PragmaticPermissionRequirement</c> in the endpoint's policy — <b>whatever the compilation
///     references</b>.
/// </summary>
/// <remarks>
///     ⚠️ The shape must not depend on a <c>ProjectReference</c> on the boundary library. An opaque
///     <c>RequireAssertion</c> gate would still refuse the request, but nothing reading endpoint
///     metadata could say what the endpoint required, and the 403 would come back without its
///     <c>requiredPermissions</c> — the result handler reads those off the requirement. The
///     requirement lives in <c>Pragmatic.Endpoints.AspNetCore</c>, which every boundary with an
///     endpoint already references, so the three <c>…WithoutIdentity…</c> tests below assert that a
///     compilation without <c>Identity.AspNetCore</c> gets the <b>same</b> shape.
/// </remarks>
public class AuthorizationBridgeTests : EndpointsGeneratorTestBase
{
    private const string EndpointSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp;

        public record SecuredResponse(string Data);

        [Endpoint(HttpVerb.Get, "/secured")]
        [RequirePermission("orders:read", "orders:list")]
        public partial class SecuredEndpoint : Endpoint<SecuredResponse>
        {
            public override Task<Result<SecuredResponse>> HandleAsync(CancellationToken ct = default)
            {
                return Task.FromResult<Result<SecuredResponse>>(new SecuredResponse("ok"));
            }
        }
        """;

    private const string AnyPermissionSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace TestApp;

        public record AnyResponse(string Data);

        [Endpoint(HttpVerb.Get, "/any-perm")]
        [RequireAnyPermission("admin:full", "orders:manage")]
        public partial class AnyPermEndpoint : Endpoint<AnyResponse>
        {
            public override Task<Result<AnyResponse>> HandleAsync(CancellationToken ct = default)
            {
                return Task.FromResult<Result<AnyResponse>>(new AnyResponse("ok"));
            }
        }
        """;

    /// <summary>
    ///     The pattern the repository encourages — a generated permission constant instead of a magic
    ///     string. `OrdersPermissions` is emitted by this same generator from the `[assembly: Permission]`
    ///     below, so it cannot be bound semantically while the endpoint is being transformed.
    /// </summary>
    private const string GeneratedConstantSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Authorization;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        [assembly: Permission("orders.read", "Read orders")]

        namespace TestApp;

        public record ConstResponse(string Data);

        [Endpoint(HttpVerb.Get, "/orders/export")]
        [RequirePermission(OrdersPermissions.Read)]
        public partial class ExportOrdersEndpoint : Endpoint<ConstResponse>
        {
            public override Task<Result<ConstResponse>> HandleAsync(CancellationToken ct = default)
            {
                return Task.FromResult<Result<ConstResponse>>(new ConstResponse("ok"));
            }
        }
        """;

    /// <summary>
    ///     A permission written as a generated constant must be enforced exactly like a literal one.
    ///     It is not enough that the endpoint requires authentication: any authenticated user would
    ///     then reach an endpoint the author marked as needing a specific permission.
    /// </summary>
    [Fact]
    public void RequirePermission_WithGeneratedConstant_StillEmitsAuthorization()
    {
        var result = RunGeneratorWithIdentity(GeneratedConstantSource);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("RequireAuthorization");
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("\"orders.read\"");
    }

    // ── Without Identity.AspNetCore ──

    /// <summary>
    ///     A boundary that does not reference Identity.AspNetCore gets the same policy as one that
    ///     does: the requirement, in the metadata, naming the permissions.
    /// </summary>
    [Fact]
    public void RequirePermission_WithoutIdentity_StillPutsTheRequirementInThePolicy()
    {
        var result = RunGenerator(EndpointSource);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("PermissionMode.All");
        handler.Should().Contain("\"orders:read\"");
        handler.Should().Contain("\"orders:list\"");

        // The shape that made the permissions unreadable, and the 403 bodiless.
        handler.Should().NotContain("RequireAssertion");
        handler.Should().NotContain(".All(p => perms.Contains(p))");
    }

    /// <summary>
    ///     The two renderings agree character for character: the reference decides nothing.
    /// </summary>
    /// <remarks>
    ///     This is the control the issue exists for. "Both contain the requirement" would also hold if
    ///     one of them still carried an extra assertion beside it.
    /// </remarks>
    [Fact]
    public void TheReferenceDecidesNothing_TheTwoRenderingsAreTheSame()
    {
        var withIdentity = GetGeneratedSource(RunGeneratorWithIdentity(EndpointSource), "Endpoint");
        var without = GetGeneratedSource(RunGenerator(EndpointSource), "Endpoint");

        without.Should().Be(withIdentity);
    }

    [Fact]
    public void RequireAnyPermission_WithoutIdentity_StillPutsTheRequirementInThePolicy()
    {
        var result = RunGenerator(AnyPermissionSource);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("PermissionMode.Any");
        handler.Should().NotContain("HasAnyPermissionAsync");
    }

    // ── With Identity.AspNetCore ──

    [Fact]
    public void RequirePermission_WithIdentity_GeneratesPragmaticPermissionRequirement()
    {
        var result = RunGeneratorWithIdentity(EndpointSource);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("PermissionMode.All");
        handler.Should().Contain("\"orders:read\"");
        handler.Should().Contain("\"orders:list\"");
        handler.Should().NotContain("RequireClaim");
    }

    [Fact]
    public void RequireAnyPermission_WithIdentity_GeneratesPragmaticPermissionRequirementAny()
    {
        var result = RunGeneratorWithIdentity(AnyPermissionSource);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("PermissionMode.Any");
        handler.Should().Contain("\"admin:full\"");
        handler.Should().Contain("\"orders:manage\"");
        handler.Should().NotContain("RequireAssertion");
    }

    // ── DomainAction with permissions ──

    [Fact]
    public void DomainAction_WithPermission_WithIdentity_GeneratesBridge()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            [RequirePermission("orders:create")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string ProductName { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
                }
            }
            """;

        var result = RunGeneratorWithIdentity(source);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("PermissionMode.All");
        handler.Should().Contain("\"orders:create\"");
    }

    [Fact]
    public void DomainAction_WithPermission_WithoutIdentity_GetsTheSameRequirement()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            [RequirePermission("orders:create")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string ProductName { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("PragmaticPermissionRequirement");
        handler.Should().Contain("PermissionMode.All");
        handler.Should().Contain("\"orders:create\"");
        handler.Should().NotContain("RequireAssertion");
    }
}
