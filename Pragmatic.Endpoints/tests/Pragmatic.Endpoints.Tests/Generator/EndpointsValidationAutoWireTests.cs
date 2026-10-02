// Pragmatic.Endpoints.Tests - Validation Auto-Wire Tests
// Tests that the Endpoints generator auto-wires ISyncValidator.Validate()
// into the generated handler when the endpoint implements ISyncValidator.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Tests for automatic validation wiring in generated endpoint handlers.
///     Verifies that endpoints implementing ISyncValidator get Validate() injected
///     before the handler/invoker, and that [NoValidation] opts out.
/// </summary>
public class EndpointsValidationAutoWireTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        using Pragmatic.Validation;
        using Pragmatic.Validation.Types;
        """;


    #region Standalone Endpoint with Validation

    /// <summary>
    ///     Verifies the generated handler includes validation when endpoint implements ISyncValidator.
    /// </summary>
    [Fact]
    public async Task StandaloneEndpoint_ImplementsISyncValidator_GeneratesValidationCall()
    {
        var source = CommonUsings + """

            namespace TestApp.Users;

            public record UserResponse(int Id, string Name, string Email);

            [Endpoint(HttpVerb.Post, "/users")]
            [HttpStatus(201)]
            public partial class CreateUserEndpoint : Endpoint<UserResponse>, ISyncValidator
            {
                public required string Name { get; set; }

                public required string Email { get; set; }

                public ValidationError Validate()
                {
                    var error = ValidationError.Valid;
                    if (string.IsNullOrEmpty(Name))
                        error = error.WithFor("Name", "validation.required");
                    return error;
                }

                public override Task<Result<UserResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse>>(new UserResponse(1, Name, Email));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateUserEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().Contain("ISyncValidator");
        handler.Should().Contain("validationResult");
        handler.Should().Contain("validationResult.IsFailure");

        await Verify(sources);
    }

    #endregion

    #region Standalone Endpoint without Validation

    /// <summary>
    ///     Verifies the generated handler does NOT include validation when endpoint
    ///     does not implement ISyncValidator.
    /// </summary>
    [Fact]
    public void StandaloneEndpoint_NoISyncValidator_NoValidationInHandler()
    {
        var source = CommonUsings + """

            namespace TestApp.Users;

            public record UserResponse(int Id, string Name);

            [Endpoint(HttpVerb.Get, "/users")]
            public partial class ListUsersEndpoint : Endpoint<UserResponse[]>
            {
                public override Task<Result<UserResponse[]>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse[]>>(Array.Empty<UserResponse>());
                }
            }
            """;

        var result = RunGenerator(source);

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().NotContain("ISyncValidator");
        handler.Should().NotContain("validationResult");
    }

    #endregion

    #region NoValidation Opt-Out

    /// <summary>
    ///     Verifies the generated handler does NOT include validation when endpoint
    ///     has [NoValidation] attribute, even if it implements ISyncValidator.
    /// </summary>
    [Fact]
    public async Task StandaloneEndpoint_WithNoValidation_SkipsValidation()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Attributes;

            namespace TestApp.Users;

            public record UserResponse(int Id, string Name);

            [Endpoint(HttpVerb.Post, "/users")]
            [NoValidation]
            [HttpStatus(201)]
            public partial class CreateUserEndpoint : Endpoint<UserResponse>, ISyncValidator
            {
                public required string Name { get; set; }

                public ValidationError Validate()
                {
                    return ValidationError.Valid;
                }

                public override Task<Result<UserResponse>> HandleAsync(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<UserResponse>>(new UserResponse(1, Name));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateUserEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().NotContain("ISyncValidator");
        handler.Should().NotContain("validationResult");

        await Verify(sources);
    }

    #endregion

    #region DomainAction Endpoint with Validation

    /// <summary>
    ///     The generated handler must NOT validate inline for a
    ///     DomainAction endpoint — the invoker pipeline's ValidationFilter (Order 100) already runs
    ///     sync validation with full semantics; an inline call would validate twice per request.
    /// </summary>
    [Fact]
    public async Task DomainActionEndpoint_ImplementsISyncValidator_DelegatesValidationToInvokerPipeline()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;

            namespace TestApp.Orders;

            public record OrderResponse(int OrderId, decimal Total);

            [Endpoint(HttpVerb.Post, "/orders")]
            [HttpStatus(201)]
            public partial class CreateOrderEndpoint : DomainAction<OrderResponse>, ISyncValidator
            {
                public required string CustomerId { get; set; }

                public required decimal Total { get; set; }

                public ValidationError Validate()
                {
                    var error = ValidationError.Valid;
                    if (string.IsNullOrEmpty(CustomerId))
                        error = error.WithFor("CustomerId", "validation.required");
                    if (Total <= 0)
                        error = error.WithFor("Total", "validation.positive");
                    return error;
                }

                public override Task<Result<OrderResponse, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<OrderResponse, IError>>(new OrderResponse(1, Total));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("CreateOrderEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().NotContain("validationResult");
        handler.Should().Contain("invoker.InvokeAsync(action, ct)");

        await Verify(sources);
    }

    #endregion

    #region DomainAction Endpoint with NoValidation

    /// <summary>
    ///     Verifies the generated handler does NOT include validation for a DomainAction
    ///     endpoint that has [NoValidation], even if it implements ISyncValidator.
    /// </summary>
    [Fact]
    public async Task DomainActionEndpoint_WithNoValidation_SkipsValidation()
    {
        var source = CommonUsings + """
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;

            namespace TestApp.Orders;

            public record ImportResult(int ProcessedCount);

            [Endpoint(HttpVerb.Post, "/orders/import")]
            [NoValidation]
            [HttpStatus(201)]
            public partial class ImportOrdersEndpoint : DomainAction<ImportResult>, ISyncValidator
            {
                public required byte[] Data { get; set; }

                public ValidationError Validate()
                {
                    return ValidationError.Valid;
                }

                public override Task<Result<ImportResult, IError>> Execute(CancellationToken ct = default)
                {
                    return Task.FromResult<Result<ImportResult, IError>>(new ImportResult(0));
                }
            }
            """;

        var result = RunGenerator(source);

        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k => k.EndsWith("ImportOrdersEndpoint.Endpoint.g.cs"), "hint names are namespace-prefixed since the W5 collision fix");

        var handler = GetGeneratedSource(result, "Endpoint");
        handler.Should().NotBeNull();
        handler.Should().NotContain("ISyncValidator");
        handler.Should().NotContain("validationResult");

        await Verify(sources);
    }

    #endregion
}
