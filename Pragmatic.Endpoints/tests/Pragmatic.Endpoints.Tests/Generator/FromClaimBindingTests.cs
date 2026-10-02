using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for [FromClaim] binding in endpoint source generation.
///     Claims are extracted from HttpContext.User inside the handler body.
/// </summary>
/// <remarks>
///     A required claim is set in the object initializer (<c>UserName = userNameClaim</c>), not assigned
///     after construction: these cases asserted <c>endpoint.UserName =</c>, the shape that is CS9035 on a
///     <c>required</c> property and CS8852 on an <c>init</c> one
///     (<see cref="AClaimOrCookieThatCompilesTests" />).
/// </remarks>
public class FromClaimBindingTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Endpoint_WithStringClaim_GeneratesClaimExtraction()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProfileResponse(string Name);

            [Endpoint(HttpVerb.Get, "/profile")]
            public partial class GetProfileEndpoint : Endpoint<ProfileResponse>
            {
                [FromClaim("name")]
                public string UserName { get; set; } = "";

                public override Task<Result<ProfileResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProfileResponse>>(new ProfileResponse(UserName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("FindFirst(\"name\")");
        handlerSource.Should().Contain("UserName = userNameClaim");
        // String claims should not have a parse call
        handlerSource.Should().NotContain("Guid.TryParse");
    }

    [Fact]
    public void Endpoint_WithGuidClaim_GeneratesParsing()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProfileResponse(Guid Id);

            [Endpoint(HttpVerb.Get, "/profile")]
            public partial class GetProfileByIdEndpoint : Endpoint<ProfileResponse>
            {
                [FromClaim("sub")]
                public Guid UserId { get; set; }

                public override Task<Result<ProfileResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProfileResponse>>(new ProfileResponse(UserId));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("FindFirst(\"sub\")");
        handlerSource.Should().Contain("Guid.TryParse");
        handlerSource.Should().Contain("UserId = userIdClaim");
    }

    [Fact]
    public void Endpoint_WithOptionalClaim_SkipsErrorOnMissing()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProfileResponse(string Name);

            [Endpoint(HttpVerb.Get, "/profile")]
            public partial class GetProfileOptionalEndpoint : Endpoint<ProfileResponse>
            {
                [FromClaim("role", IsRequired = false)]
                public string? Role { get; set; }

                public override Task<Result<ProfileResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProfileResponse>>(new ProfileResponse(Role ?? "unknown"));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("FindFirst(\"role\")");
        // Optional claims should not have 401 error return
        handlerSource.Should().NotContain("statusCode: 401");
        // Optional string claims should use conditional assignment
        handlerSource.Should().Contain("is not null");
    }

    [Fact]
    public void Endpoint_WithRequiredClaim_GeneratesUnauthorizedReturn()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProfileResponse(string Name);

            [Endpoint(HttpVerb.Get, "/profile")]
            public partial class GetProfileRequiredEndpoint : Endpoint<ProfileResponse>
            {
                [FromClaim("sub")]
                public string UserId { get; set; } = "";

                public override Task<Result<ProfileResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProfileResponse>>(new ProfileResponse(UserId));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Required claims should return 401 if missing
        handlerSource.Should().Contain("statusCode: 401");
        handlerSource.Should().Contain("Missing required claim: sub");
    }

    [Fact]
    public void Endpoint_WithRequiredTypedClaim_RejectsMalformedValue()
    {
        // A present-but-malformed required typed claim must be rejected (401),
        // not silently coerced to Guid.Empty/0/false.
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProfileResponse(Guid Id);

            [Endpoint(HttpVerb.Get, "/profile")]
            public partial class GetProfileStrictEndpoint : Endpoint<ProfileResponse>
            {
                [FromClaim("sub")]
                public Guid UserId { get; set; }

                public override Task<Result<ProfileResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProfileResponse>>(new ProfileResponse(UserId));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // Guards the parse and rejects on failure...
        handlerSource.Should().Contain("if (!System.Guid.TryParse");
        handlerSource.Should().Contain("Invalid required claim: sub");
        handlerSource.Should().Contain("statusCode: 401");
        // ...and assigns the PARSED value (not a coerced default expression).
        handlerSource.Should().Contain("UserId = userIdClaimParsed");
        handlerSource.Should().NotContain("? userIdClaimParsed : default");
    }

    [Fact]
    public void Endpoint_WithMultipleClaims_GeneratesAllBindings()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record ProfileResponse(Guid Id, string Name);

            [Endpoint(HttpVerb.Get, "/profile")]
            public partial class GetFullProfileEndpoint : Endpoint<ProfileResponse>
            {
                [FromClaim("sub")]
                public Guid UserId { get; set; }

                [FromClaim("name")]
                public string UserName { get; set; } = "";

                [FromClaim("role", IsRequired = false)]
                public string? Role { get; set; }

                public override Task<Result<ProfileResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ProfileResponse>>(new ProfileResponse(UserId, UserName));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("FindFirst(\"sub\")");
        handlerSource.Should().Contain("FindFirst(\"name\")");
        handlerSource.Should().Contain("FindFirst(\"role\")");
        handlerSource.Should().Contain("UserId = userIdClaim");
        handlerSource.Should().Contain("UserName = userNameClaim");
    }

    [Fact]
    public void DomainAction_WithClaim_GeneratesClaimExtraction()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record OrderId(int Value);

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class CreateOrderAction : DomainAction<OrderId>
            {
                [FromClaim("sub")]
                public Guid CreatedBy { get; set; }

                public required string ProductName { get; init; }

                public override Task<Result<OrderId, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderId, IError>>(new OrderId(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        handlerSource.Should().Contain("FindFirst(\"sub\")");
        handlerSource.Should().Contain("CreatedBy = createdByClaim");
        handlerSource.Should().Contain("Guid.TryParse");
    }

    [Fact]
    public void Endpoint_WithClaim_ExcludesClaimFromBody()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp;

            public record CreateResponse(int Id);

            [Endpoint(HttpVerb.Post, "/items")]
            public partial class CreateItemEndpoint : Endpoint<CreateResponse>
            {
                [FromClaim("sub")]
                public Guid UserId { get; set; }

                public required string Name { get; set; }

                public override Task<Result<CreateResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<CreateResponse>>(new CreateResponse(1));
                }
            }
            """;

        var result = RunGenerator(source);

        var handlerSource = GetGeneratedSource(result, "Endpoint");
        handlerSource.Should().NotBeNull();
        // UserId should NOT appear in the body DTO (it comes from claim)
        var bodyDtoSource = GetGeneratedSource(result, "Body");
        if (bodyDtoSource is not null)
        {
            bodyDtoSource.Should().NotContain("UserId");
            bodyDtoSource.Should().Contain("Name");
        }
    }
}
