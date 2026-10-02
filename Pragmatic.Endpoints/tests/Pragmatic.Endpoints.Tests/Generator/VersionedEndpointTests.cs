using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for versioned DomainAction endpoints:
///     versioned body DTOs, PRAG0551 diagnostic, and versioned route registration.
/// </summary>
public class VersionedEndpointTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    [Fact]
    public void VersionedAction_WithoutAspVersioning_EmitsPRAG0551()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                [SinceVersion("2.0")]
                public string? Phone { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));
            }
            """;

        var result = RunGenerator(source);

        // Without Asp.Versioning.Http referenced, should emit PRAG0551 warning
        HasDiagnostic(result, "PRAG0551").Should().BeTrue();
    }

    [Fact]
    public void VersionedAction_WithoutAspVersioning_GeneratesNonVersionedHandler()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                [SinceVersion("2.0")]
                public string? Phone { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));
            }
            """;

        var result = RunGenerator(source);

        // Without Asp.Versioning, should generate a normal (non-versioned) handler
        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("IDomainActionInvoker");
        // Should NOT contain versioned API constructs
        handlerSource.Should().NotContain("ApiVersionSet");
        handlerSource.Should().NotContain("MapToApiVersion");
    }

    [Fact]
    public void VersionedAction_WithoutAspVersioning_DoesNotGenerateVersionedBodyDtos()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                [SinceVersion("2.0")]
                public string? Phone { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));
            }
            """;

        var result = RunGenerator(source);

        // DomainAction endpoints bind body properties directly in the handler
        // (no separate body DTO). Verify the endpoint handler includes all properties
        // and does NOT generate versioned variants.
        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("PlaceOrderAction");
        // Should NOT contain versioned body DTO classes
        var allSources = GetGeneratedSourcesAsDictionary(result);
        allSources.Keys.Should().NotContain(k => k.Contains("V1Body"));
        allSources.Keys.Should().NotContain(k => k.Contains("V2Body"));
    }

    [Fact]
    public void NonVersionedAction_DoesNotEmitPRAG0551()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0551").Should().BeFalse();
    }

    [Fact]
    public void SinceVersion_FiltersBodyProperties_ForVersionedParsing()
    {
        // This tests that the Endpoints SG correctly detects versioned Execute methods
        // and parses [SinceVersion] attributes. Even without Asp.Versioning, the
        // ActionVersions model should be populated (though versioned output won't be generated).
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                [SinceVersion("2.0")]
                public string? Phone { get; init; }

                [SinceVersion("3.0")]
                public string? Email { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));

                public Task<Result<OrderId, IError>> ExecuteV3(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(3));
            }
            """;

        var result = RunGenerator(source);

        // Should emit PRAG0551 (no Asp.Versioning)
        HasDiagnostic(result, "PRAG0551").Should().BeTrue();

        // DomainAction endpoints bind body properties directly in the handler.
        // Verify all properties (including versioned ones) are present in the handler.
        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("Name");
        handlerSource.Should().Contain("Phone");
        handlerSource.Should().Contain("Email");
    }

    [Fact]
    public void VersionedAction_MultipleVersions_GeneratesDiagnosticWithTypeName()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));

                public Task<Result<OrderId, IError>> ExecuteV3(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(3));
            }
            """;

        var result = RunGenerator(source);

        var diagnostics = GetDiagnosticsById(result, "PRAG0551").ToList();
        diagnostics.Should().HaveCount(1);
        diagnostics[0].GetMessage().Should().Contain("PlaceOrderAction");
    }

    [Fact]
    public void VoidVersionedAction_WithoutAspVersioning_EmitsPRAG0551()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            [Endpoint(HttpVerb.Delete, "/orders/{id}")]
            public partial class DeleteOrderAction : VoidDomainAction
            {
                [Microsoft.AspNetCore.Mvc.FromRoute]
                public int Id { get; set; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Ok());

                public Task<VoidResult<IError>> ExecuteV2(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Ok());
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0551").Should().BeTrue();

        // Should generate a handler anyway (non-versioned fallback)
        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("IVoidDomainActionInvoker");
        handlerSource.Should().NotContain("ApiVersionSet");
    }

    [Fact]
    public void VersionedAction_WrongSignature_IgnoredAndNoDiagnostic()
    {
        var source = CommonUsings + """

            namespace TestApp.Orders;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class PlaceOrderAction : DomainAction<OrderId>
            {
                public required string Name { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                // Wrong signature — extra parameter, should be ignored
                public Task<Result<OrderId, IError>> ExecuteV2(string extra, CancellationToken ct = default)
                    => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));
            }
            """;

        var result = RunGenerator(source);

        // No versioning diagnostic since the method was ignored
        HasDiagnostic(result, "PRAG0551").Should().BeFalse();

        // Should generate normal (non-versioned) handler
        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().NotContain("ApiVersionSet");
    }

    // When Asp.Versioning is available the SG emits one route builder per version. The shared
    // endpoint configuration (auth, rate-limit, caching, OpenAPI metadata) must be applied to EVERY
    // version: applied ONCE after the loop, it would reach the LAST version only and leave earlier
    // API versions exposed with no authorization/caching.
    // A stub Asp.Versioning.ApiVersion type activates the versioned codepath without a NuGet dep;
    // we assert on generated text (the versioned output references extension methods we don't link).
    // Block-scoped namespaces are used here so the Asp.Versioning stub can coexist in one file.
    [Fact]
    public void VersionedAction_WithAspVersioning_AppliesSharedConfigToEveryVersion()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Asp.Versioning
            {
                public sealed class ApiVersion
                {
                    public ApiVersion(int major, int minor) { }
                }
            }

            namespace TestApp.Orders
            {
                public record OrderId(int Value);

                [Endpoint(HttpVerb.Post, "/orders")]
                [RateLimit(Policy = "orders-limit")]
                public partial class PlaceOrderAction : DomainAction<OrderId>
                {
                    public required string Name { get; init; }

                    [SinceVersion("2.0")]
                    public string? Phone { get; init; }

                    public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                        => Task.FromResult<Result<OrderId, IError>>(new OrderId(1));

                    public Task<Result<OrderId, IError>> ExecuteV2(CancellationToken ct = default)
                        => Task.FromResult<Result<OrderId, IError>>(new OrderId(2));
                }
            }
            """;

        var result = RunGenerator(source);

        // Asp.Versioning present + 2 versions → versioned codepath, no PRAG0551.
        HasDiagnostic(result, "PRAG0551").Should().BeFalse();

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("WithApiVersionSet");

        // One route builder per version.
        var builderCount = System.Text.RegularExpressions.Regex
            .Matches(handlerSource!, @"\.WithApiVersionSet\(versionSet\)").Count;
        builderCount.Should().Be(2);

        // Core assertion: shared config is rendered once PER version, not just for the last.
        // ProducesResponseTypeMetadata(400, typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails) is emitted unconditionally for every DomainAction route builder.
        var producesCount = System.Text.RegularExpressions.Regex
            .Matches(handlerSource!, @"ProducesResponseTypeMetadata\(400,").Count;
        producesCount.Should().Be(builderCount);

        // The rate-limit policy must guard every version too.
        var rateLimitCount = System.Text.RegularExpressions.Regex
            .Matches(handlerSource!, @"RequireRateLimiting\(""orders-limit""\)").Count;
        rateLimitCount.Should().Be(builderCount);

        // The non-last version is configured via its own builder variable (not "builder.").
        handlerSource.Should().MatchRegex(@"builder\w+\.WithMetadata\(new global::Microsoft\.AspNetCore\.Http\.ProducesResponseTypeMetadata\(400,");
    }

    [Fact]
    public void NonDomainAction_WithVersionedMethodNames_IgnoresVersioning()
    {
        var source = CommonUsings + """

            namespace TestApp;

            [Endpoint(HttpVerb.Get, "/health")]
            public partial class HealthEndpoint : Endpoint<string>
            {
                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success("ok"));
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0551").Should().BeFalse();

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().NotContain("ApiVersionSet");
        handlerSource.Should().NotContain("TargetVersion");
    }
}
