using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Pipeline;

namespace Pragmatic.Resilience.Tests.Unit;

public class PassthroughPipelineTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public void Instance_IsSingleton()
    {
        PassthroughPipeline.Instance.Should().BeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_Generic_ReturnsOperationResultUnchanged()
    {
        var result = await PassthroughPipeline.Instance.ExecuteAsync(
            (_, _) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_Generic_InvokesOperationExactlyOnce()
    {
        var calls = 0;

        await PassthroughPipeline.Instance.ExecuteAsync(
            (_, _) => { calls++; return Task.FromResult(1); },
            CreateContext(), CancellationToken.None);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Generic_PassesContextAndTokenThrough()
    {
        var context = CreateContext("MyOp");
        using var cts = new CancellationTokenSource();
        ResilienceContext? receivedCtx = null;
        CancellationToken receivedToken = default;

        await PassthroughPipeline.Instance.ExecuteAsync(
            (ctx, ct) => { receivedCtx = ctx; receivedToken = ct; return Task.FromResult(0); },
            context, cts.Token);

        receivedCtx.Should().BeSameAs(context);
        receivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_Generic_DoesNotSwallowExceptions()
    {
        var act = () => PassthroughPipeline.Instance.ExecuteAsync<int>(
            (_, _) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_Void_InvokesOperation()
    {
        var executed = false;

        await PassthroughPipeline.Instance.ExecuteAsync(
            (_, _) => { executed = true; return Task.CompletedTask; },
            CreateContext(), CancellationToken.None);

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Void_DoesNotSwallowExceptions()
    {
        var act = () => PassthroughPipeline.Instance.ExecuteAsync(
            (_, _) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
