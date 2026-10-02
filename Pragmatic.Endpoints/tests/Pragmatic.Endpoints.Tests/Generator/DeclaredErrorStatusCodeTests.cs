using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An action declares the errors it can return on its base type — <c>VoidDomainAction&lt;TError&gt;</c>,
///     <c>DomainAction&lt;TReturn, TError…&gt;</c>, each of which implements <c>IProducesError&lt;…&gt;</c>.
///     The OpenAPI document must say so.
/// </summary>
/// <remarks>
///     A <c>Mutation&lt;T, ConflictError&gt;</c> and an <c>[Endpoint]</c> class document their 409, and
///     so must the same declaration on a <c>VoidDomainAction&lt;ConflictError&gt;</c>. A domain-action
///     template that built its status set from constants, without reading the declared errors, would
///     document 400/401/403/500 and nothing else: the same contract, described two different ways
///     depending on which base the author picked.
///     <para>
///         These are the cases that fail without the loop over the declared errors.
///     </para>
/// </remarks>
public class DeclaredErrorStatusCodeTests : EndpointsGeneratorTestBase
{
    private const string VoidActionWithConflict = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;
        using Pragmatic.Result.Http;

        namespace TestApp;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "/invoices/{id}/refund")]
        public partial class RefundInvoiceAction : VoidDomainAction<ConflictError>
        {
            [Microsoft.AspNetCore.Mvc.FromRoute]
            public System.Guid Id { get; set; }

            public override Task<VoidResult<ConflictError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<ConflictError>.Success());
        }
        """;

    private const string ActionWithTwoErrors = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;
        using Pragmatic.Result.Http;

        namespace TestApp;

        [DomainAction]
        [Endpoint(HttpVerb.Get, "/invoices/{id}")]
        public partial class GetInvoiceAction : DomainAction<string, NotFoundError, ConflictError>
        {
            [Microsoft.AspNetCore.Mvc.FromRoute]
            public System.Guid Id { get; set; }

            public override Task<Result<string, NotFoundError, ConflictError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, NotFoundError, ConflictError>.Success("ok"));
        }
        """;

    [Fact]
    public void VoidDomainAction_WithDeclaredError_DocumentsItsStatusCode()
    {
        var handler = GetGeneratedSource(RunGenerator(VoidActionWithConflict), "Endpoint");

        handler.Should().NotBeNull();
        handler!.Should().Contain("ProducesResponseTypeMetadata(409");
    }

    [Fact]
    public void DomainAction_WithSeveralDeclaredErrors_DocumentsEachStatusCode()
    {
        var handler = GetGeneratedSource(RunGenerator(ActionWithTwoErrors), "Endpoint");

        handler.Should().NotBeNull();
        handler!.Should()
            .Contain("ProducesResponseTypeMetadata(404")
            .And.Contain("ProducesResponseTypeMetadata(409");
    }

    [Fact]
    public void DomainAction_DeclaredErrors_StillDocumentTheFallbackContract()
    {
        // The declared codes are added to the pipeline's own, not substituted for them: validation
        // still returns 400 and an unhandled failure still returns 500.
        var handler = GetGeneratedSource(RunGenerator(VoidActionWithConflict), "Endpoint");

        handler.Should().NotBeNull();
        handler!.Should()
            .Contain("ProducesResponseTypeMetadata(400")
            .And.Contain("ProducesResponseTypeMetadata(500");
    }

    private const string EndpointWithTypedError = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        using Pragmatic.Result.Http;

        namespace TestApp;

        [Endpoint(HttpVerb.Get, "/invoices/{id}")]
        public partial class GetInvoiceEndpoint : Endpoint<string, NotFoundError>
        {
            [Microsoft.AspNetCore.Mvc.FromRoute]
            public System.Guid Id { get; set; }

            public override Task<Result<string, NotFoundError>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult<Result<string, NotFoundError>>("ok");
        }
        """;

    private const string StreamingEndpointWithTypedError = """
        using System.Collections.Generic;
        using System.Threading;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        using Pragmatic.Result.Http;

        namespace TestApp;

        [Endpoint(HttpVerb.Get, "/reservations/feed")]
        public partial class StreamFeedEndpoint : StreamingEndpoint<string, NotFoundError>
        {
            public override async IAsyncEnumerable<Result<string, NotFoundError>> HandleAsync(
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield return "one";
            }
        }
        """;

    [Fact]
    public void Endpoint_WithTypedError_DocumentsTheErrorTypeSchema()
    {
        var handler = GetGeneratedSource(RunGenerator(EndpointWithTypedError), "Endpoint");

        handler.Should().NotBeNull();
        // The error TYPE, not ProblemDetails, even though ProblemDetails is what goes on the wire:
        // naming the type is what gives OpenAPI a schema per error for ErrorSchemaEnricher to reshape
        // into RFC 7807 with that error's own properties as extensions.
        handler!.Should()
            .Contain("ProducesResponseTypeMetadata(404, typeof(global::Pragmatic.Result.Http.NotFoundError))");
    }

    [Fact]
    public void StreamingEndpoint_WithTypedError_DocumentsNoErrorStatus()
    {
        var handler = GetGeneratedSource(RunGenerator(StreamingEndpointWithTypedError), "Endpoint");

        handler.Should().NotBeNull();
        // Failures travel in band, as an SseStreamEvent.FromError on a response that already answered
        // 200. The generated handler has no path that returns 404, so the document must not claim one.
        handler!.Should()
            .Contain("ProducesResponseTypeMetadata(200, typeof(string), new[] { \"text/event-stream\" })")
            .And.NotContain("ProducesResponseTypeMetadata(404");
    }
}
