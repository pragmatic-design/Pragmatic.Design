using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Pragmatic.Endpoints.Context;
using Pragmatic.Endpoints.Processors;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     Verifies the default-interface dispatch in <see cref="IEndpointPreProcessor{TEndpoint}"/>
///     and <see cref="IEndpointPostProcessor{TEndpoint}"/>: when the context endpoint matches
///     <c>TEndpoint</c>, the untyped <c>ProcessAsync</c> delegates to the typed overload.
/// </summary>
public class EndpointProcessorDispatchTests
{
    private sealed class SampleEndpoint;

    private static IEndpointContext ContextFor(object endpoint)
        => new EndpointContext(new DefaultHttpContext(), "Sample", endpoint);

    // ── Pre-processor ──

    private sealed class TypedPreProcessor : IEndpointPreProcessor<SampleEndpoint>
    {
        public SampleEndpoint? ReceivedEndpoint { get; private set; }

        public ValueTask<PreProcessorResult> ProcessAsync(
            SampleEndpoint endpoint, IEndpointContext context, CancellationToken ct = default)
        {
            ReceivedEndpoint = endpoint;
            return ValueTask.FromResult(PreProcessorResult.Continue());
        }
    }

    private sealed class ShortCircuitPreProcessor : IEndpointPreProcessor<SampleEndpoint>
    {
        public ValueTask<PreProcessorResult> ProcessAsync(
            SampleEndpoint endpoint, IEndpointContext context, CancellationToken ct = default)
            => ValueTask.FromResult(PreProcessorResult.NotFound("Sample", "1"));
    }

    [Fact]
    public async Task PreProcessor_EndpointMatchesType_DelegatesToTypedOverload()
    {
        var endpoint = new SampleEndpoint();
        var processor = new TypedPreProcessor();

        await ((IEndpointPreProcessor)processor).ProcessAsync(ContextFor(endpoint), CancellationToken.None);

        processor.ReceivedEndpoint.Should().BeSameAs(endpoint);
    }

    [Fact]
    public async Task PreProcessor_TypedReturnsContinue_PropagatesShouldContinue()
    {
        var processor = new TypedPreProcessor();

        var result = await ((IEndpointPreProcessor)processor)
            .ProcessAsync(ContextFor(new SampleEndpoint()), CancellationToken.None);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task PreProcessor_TypedShortCircuits_PropagatesError()
    {
        var processor = new ShortCircuitPreProcessor();

        var result = await ((IEndpointPreProcessor)processor)
            .ProcessAsync(ContextFor(new SampleEndpoint()), CancellationToken.None);

        result.ShouldContinue.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    // ── Post-processor ──

    private sealed class TypedPostProcessor : IEndpointPostProcessor<SampleEndpoint>
    {
        public SampleEndpoint? ReceivedEndpoint { get; private set; }
        public object? ReceivedResult { get; private set; }

        public ValueTask ProcessAsync(
            SampleEndpoint endpoint, IEndpointContext context, object? result, CancellationToken ct = default)
        {
            ReceivedEndpoint = endpoint;
            ReceivedResult = result;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task PostProcessor_EndpointMatchesType_DelegatesToTypedOverload()
    {
        var endpoint = new SampleEndpoint();
        var processor = new TypedPostProcessor();

        await ((IEndpointPostProcessor)processor)
            .ProcessAsync(ContextFor(endpoint), result: "ok", CancellationToken.None);

        processor.ReceivedEndpoint.Should().BeSameAs(endpoint);
    }

    [Fact]
    public async Task PostProcessor_PassesHandlerResultToTypedOverload()
    {
        var processor = new TypedPostProcessor();
        var handlerResult = new object();

        await ((IEndpointPostProcessor)processor)
            .ProcessAsync(ContextFor(new SampleEndpoint()), handlerResult, CancellationToken.None);

        processor.ReceivedResult.Should().BeSameAs(handlerResult);
    }
}
