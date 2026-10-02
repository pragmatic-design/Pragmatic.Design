using Pragmatic.Testing.Assertions;
using Pragmatic.Actions.Pipeline;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     <see cref="ActionCallContext"/> internal-call state must be flow-local
///     (AsyncLocal), not shared across the DI scope. Two concurrent action flows in the SAME scope
///     must not leak <see cref="ActionCallContext.IsInternalCall"/> into each other, or an external
///     call could skip permission/policy checks.
/// </summary>
public class ActionCallContextFlowLocalTests
{
    [Fact]
    public void EnterInternalCall_SetsAndResets_Nested()
    {
        var ctx = new ActionCallContext();
        ctx.IsInternalCall.Should().BeFalse();

        using (ctx.EnterInternalCall())
        {
            ctx.IsInternalCall.Should().BeTrue();
            using (ctx.EnterInternalCall())
            {
                ctx.IsInternalCall.Should().BeTrue();
            }

            // Inner scope disposed — still internal because the outer scope is active.
            ctx.IsInternalCall.Should().BeTrue();
        }

        ctx.IsInternalCall.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentFlows_InSameScope_DoNotLeakInternalBypass()
    {
        // Single shared context (as a scoped DI registration would be).
        var ctx = new ActionCallContext();

        // Flow A enters internal-call mode and parks. Flow B never enters it and must observe
        // IsInternalCall == false for its entire lifetime — proving no cross-flow leak.
        var flowBStarted = new TaskCompletionSource();
        var flowAEntered = new TaskCompletionSource();

        var flowB = Task.Run(async () =>
        {
            flowBStarted.SetResult();
            // Wait until flow A is firmly inside its internal-call scope.
            await flowAEntered.Task.ConfigureAwait(false);

            // The external flow B must NOT inherit flow A's internal-call state.
            ctx.IsInternalCall.Should().BeFalse(
                "internal-call state is flow-local and must not bleed across concurrent flows in the same scope");
        });

        var flowA = Task.Run(async () =>
        {
            await flowBStarted.Task.ConfigureAwait(false);
            using (ctx.EnterInternalCall())
            {
                ctx.IsInternalCall.Should().BeTrue();
                flowAEntered.SetResult();
                await flowB.ConfigureAwait(false);
            }
        });

        await Task.WhenAll(flowA, flowB);

        // After both flows complete, the context is back to the default (external) state.
        ctx.IsInternalCall.Should().BeFalse();
    }
}
