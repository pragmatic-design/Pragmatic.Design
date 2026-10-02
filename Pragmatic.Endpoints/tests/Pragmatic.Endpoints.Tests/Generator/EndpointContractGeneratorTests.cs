using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for the <c>EndpointContractTemplate</c> — verifies that
///     <c>[PragmaticEndpointContract]</c> attributes are generated correctly
///     with route, method, error types, permissions, and status codes.
/// </summary>
public class EndpointContractGeneratorTests : EndpointsGeneratorTestBase
{
    // =========================================================================
    // Basic contract emission
    // =========================================================================

    [Fact]
    public void SimpleGetEndpoint_EmitsContractWithRouteAndMethod()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            public record UserDto(int Id, string Name);

            [Endpoint(HttpVerb.Get, "/users/{id}")]
            public partial class GetUserEndpoint : Endpoint<UserDto>
            {
                public override Task<Result<UserDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<UserDto>>(new UserDto(1, "Test"));
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().NotBeNull();
        contractSource.Should().Contain("PragmaticEndpointContract(");
        contractSource.Should().Contain("EndpointType = typeof(global::TestApp.Endpoints.GetUserEndpoint)");
        contractSource.Should().Contain("HttpMethod = \"GET\"");
        contractSource.Should().Contain("Route = \"/users/{id}\"");
        contractSource.Should().Contain("SuccessStatusCode = 200");
    }

    [Fact]
    public void PostEndpoint_EmitsCreated201StatusCode()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Post, "/orders")]
            public partial class CreateOrderEndpoint : Endpoint<int>
            {
                public override Task<Result<int>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<int>>(42);
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("HttpMethod = \"POST\"");
        contractSource.Should().Contain("SuccessStatusCode = 201");
    }

    [Fact]
    public void VoidEndpoint_EmitsIsVoidTrue_And204StatusCode()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Delete, "/orders/{id}")]
            public partial class DeleteOrderEndpoint : VoidEndpoint
            {
                public override Task HandleAsync(CancellationToken ct = default)
                    => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("IsVoid = true");
        contractSource.Should().Contain("SuccessStatusCode = 204");
        contractSource.Should().Contain("HttpMethod = \"DELETE\"");
    }

    // =========================================================================
    // Error types
    // =========================================================================

    [Fact]
    public void EndpointWithErrorTypes_EmitsParallelErrorArrays()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            public record NotFoundError : IError { public string Message => "Not found"; }
            public record ValidationError : IError { public string Message => "Invalid"; }

            [Endpoint(HttpVerb.Get, "/orders/{id}")]
            public partial class GetOrderEndpoint : Endpoint<int, NotFoundError>
            {
                public override Task<Result<int, NotFoundError>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<int, NotFoundError>>(404);
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("ErrorTypes = new System.Type[]");
        contractSource.Should().Contain("ErrorStatusCodes = new int[]");
        contractSource.Should().Contain("typeof(global::TestApp.Endpoints.NotFoundError)");
        contractSource.Should().Contain("404");
    }

    /// <summary>
    ///     The typed DomainAction bases exist in six arities and each implements IProducesError —
    ///     and nothing read them, so an action's domain errors never reached the contract. Three
    ///     consumer projects shipped APIs whose 422s appeared nowhere.
    /// </summary>
    [Fact]
    public void DomainActionWithDeclaredErrors_EmitsThemOnTheContract()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Actions;

            public record QualificationExpiredError : IError { public string Message => "Expired"; }

            [DomainAction]
            [Endpoint(HttpVerb.Post, "/assignments")]
            public partial class CreateAssignmentAction : DomainAction<Guid, QualificationExpiredError>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<Guid, IError>>(Guid.Empty);
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("typeof(global::TestApp.Actions.QualificationExpiredError)",
            "the base declares the error, so the contract has to carry it");
        contractSource.Should().NotContain("ErrorTypes = new System.Type[0]");
    }

    /// <summary>
    ///     Permissions have had a registry for years; the errors had none, so "which failures can this
    ///     boundary produce" could only be answered by reading every endpoint.
    /// </summary>
    [Fact]
    public void ErrorRegistry_ListsEveryDeclaredErrorOnce()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            public record MissingError : IError { public string Message => "gone"; }

            [Endpoint(HttpVerb.Get, "/a/{id}")]
            public partial class GetA : Endpoint<int, MissingError>
            {
                public override Task<Result<int, MissingError>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<int, MissingError>>(1);
            }

            [Endpoint(HttpVerb.Get, "/b/{id}")]
            public partial class GetB : Endpoint<int, MissingError>
            {
                public override Task<Result<int, MissingError>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<int, MissingError>>(2);
            }
            """;

        var contract = GetGeneratedSource(RunGenerator(source), "EndpointContracts");

        contract.Should().Contain("class ErrorRegistry");
        contract.Should().Contain("typeof(global::TestApp.Endpoints.MissingError)");

        // Two endpoints declare the same error: it belongs in the registry once.
        var occurrences = contract!.Split("ErrorRegistry")[1];
        occurrences.Split("typeof(global::TestApp.Endpoints.MissingError)").Length.Should().Be(2,
            "the registry deduplicates by type");
    }

    /// <summary>The untyped base declares nothing, and the contract must not invent anything.</summary>
    [Fact]
    public void DomainActionWithoutDeclaredErrors_EmitsNone()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Actions;

            [DomainAction]
            [Endpoint(HttpVerb.Post, "/plain")]
            public partial class PlainAction : DomainAction<Guid>
            {
                public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<Guid, IError>>(Guid.Empty);
            }
            """;

        var contractSource = GetGeneratedSource(RunGenerator(source), "EndpointContracts");

        contractSource.Should().Contain("ErrorTypes = new System.Type[0]");
    }

    [Fact]
    public void EndpointWithNoErrors_EmitsEmptyArrays()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Get, "/ping")]
            public partial class PingEndpoint : Endpoint<string>
            {
                public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<string>>("pong");
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("ErrorTypes = new System.Type[0]");
        contractSource.Should().Contain("ErrorStatusCodes = new int[0]");
    }

    // =========================================================================
    // Permissions
    // =========================================================================

    [Fact]
    public void EndpointWithRequirePermission_EmitsPermissionsArray()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Post, "/admin/users")]
            [RequirePermission("users:write")]
            public partial class CreateAdminUserEndpoint : Endpoint<int>
            {
                public override Task<Result<int>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<int>>(1);
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("RequiredPermissions = new string[]");
        contractSource.Should().Contain("\"users:write\"");
    }

    // =========================================================================
    // Multiple endpoints in same assembly
    // =========================================================================

    [Fact]
    public void MultipleEndpoints_EmitsOneContractPerEndpoint()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Get, "/users")]
            public partial class ListUsersEndpoint : Endpoint<string>
            {
                public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<string>>("ok");
            }

            [Endpoint(HttpVerb.Post, "/users")]
            public partial class CreateUserEndpoint : Endpoint<int>
            {
                public override Task<Result<int>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<int>>(1);
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().NotBeNull();

        // Count occurrences of PragmaticEndpointContract
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            contractSource!, "\\[assembly: PragmaticEndpointContract").Count;

        occurrences.Should().Be(2);
        contractSource.Should().Contain("ListUsersEndpoint");
        contractSource.Should().Contain("CreateUserEndpoint");
    }

    // =========================================================================
    // Summary and Tags
    // =========================================================================

    [Fact]
    public void EndpointWithSummary_EmitsSummaryInContract()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Get, "/products")]
            [ApiSummary("List all products")]
            public partial class ListProductsEndpoint : Endpoint<string>
            {
                public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<string>>("ok");
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("Summary = \"List all products\"");
    }

    [Fact]
    public void EndpointWithTags_EmitsTagsInContract()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace TestApp.Endpoints;

            [Endpoint(HttpVerb.Get, "/catalog")]
            [ApiTags("catalog", "public")]
            public partial class GetCatalogEndpoint : Endpoint<string>
            {
                public override Task<Result<string>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult<Result<string>>("ok");
            }
            """;

        var result = RunGenerator(source);

        var contractSource = GetGeneratedSource(result, "EndpointContracts");
        contractSource.Should().Contain("Tags = new string[]");
        contractSource.Should().Contain("\"catalog\"");
        contractSource.Should().Contain("\"public\"");
    }
}
