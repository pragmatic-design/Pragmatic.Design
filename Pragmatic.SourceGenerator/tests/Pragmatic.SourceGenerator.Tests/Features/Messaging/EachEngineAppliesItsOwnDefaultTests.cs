using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     What the declaration does not say, the engine that reads it decides.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Retry]</c> is declared once, in <c>Pragmatic.Resilience</c>, and carries no numbers of
///         its own: a job's base delay and a redelivery's are both deliberate, so neither belongs on a
///         shared attribute. Each engine substitutes its own for whatever the caller left unwritten.
///     </para>
///     <para>
///         ⚠️ This half was broken before the declarations were merged, and it read as correct:
///         <c>attribute?.GetNamedArgument&lt;int&gt;(name) ?? 200</c> looks like «200 when unwritten»
///         but yields 0, because an unwritten argument reads as 0 and 0 is not null. The fallback
///         fired only when the whole attribute was missing, so a partial <c>[Retry]</c> on a handler
///         asked for a zero delay — a well-formed pipeline with the wrong numbers in it.
///     </para>
/// </remarks>
public class EachEngineAppliesItsOwnDefaultTests
{
    private const string Stubs = """
        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
            public sealed record MessageContext(string MessageId);
            public interface IMessageHandler<in T>
            {
                System.Threading.Tasks.Task HandleAsync(T message, MessageContext context, System.Threading.CancellationToken ct = default);
            }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }
        }

        namespace Pragmatic.Resilience.Attributes
        {
            public enum BackoffStrategy { Unspecified = 0, Fixed = 1, Exponential = 2, ExponentialWithJitter = 3 }

            [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
            public sealed class RetryAttribute : System.Attribute
            {
                public int MaxAttempts { get; set; }
                public BackoffStrategy Strategy { get; set; }
                public int BaseDelayMs { get; set; }
            }
        }
        """;

    private static string PipelineFor(string retryDeclaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            {{Stubs}}

            namespace TestApp
            {
                public sealed class OrderPlaced { }

                [Pragmatic.Messaging.Attributes.MessageHandler]
                {{retryDeclaration}}
                public sealed partial class OrderHandler : Pragmatic.Messaging.IMessageHandler<OrderPlaced>
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """, []);

        return GeneratorTestHelper.GetGeneratedSource(result, "TestApp.OrderHandler.Pipeline")
               ?? throw new InvalidOperationException("the pipeline was not generated at all");
    }

    /// <summary>
    ///     A declaration that names only the attempt count gets this engine's delay, not zero.
    /// </summary>
    [Fact]
    public void WithOnlyMaxAttemptsWritten_TheHandlerGetsMessagingSBaseDelay()
    {
        var pipeline = PipelineFor("[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 2)]");

        // The shift is bounded, so the assertion stops at the base delay: the number under test is
        // 200, not the bound, and pinning the bound here would make this fail for the wrong reason.
        pipeline.Should().Contain("200 * (1 << ",
            "an unwritten base delay is the messaging engine's 200 ms, not the zero an unset argument reads as");
        pipeline.Should().NotContain("var __delay = 0",
            "which is what the unwritten argument produced before");
    }

    /// <summary>
    ///     The control: what the caller does write is what is used.
    /// </summary>
    /// <remarks>
    ///     Without it, "the default is applied" is satisfied by an engine that ignores the declaration
    ///     entirely and always writes its own numbers.
    /// </remarks>
    [Fact]
    public void WhatTheCallerWrites_BeatsTheEngineSDefault()
    {
        var pipeline = PipelineFor(
            "[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 2, BaseDelayMs = 750, Strategy = Pragmatic.Resilience.Attributes.BackoffStrategy.Fixed)]");

        pipeline.Should().Contain("var __delay = 750;", "Fixed means the base delay, every time");
        pipeline.Should().NotContain("200", "the engine's default must not survive an explicit value");
    }

    /// <summary>
    ///     The second control: the strategy is decoded by the value it actually has.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The renumbering that added <c>Unspecified = 0</c> shifted every other member by one. A
    ///     reader that switched on the old numbers would still compile, still generate, and pick the
    ///     neighbouring strategy for every declaration — which is why this asserts the shape of the
    ///     computed delay and not just that something was generated.
    /// </remarks>
    [Fact]
    public void ExponentialWithJitter_IsDecodedAsJitterAndNotAsItsNeighbour()
    {
        var pipeline = PipelineFor(
            "[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 2, BaseDelayMs = 500, Strategy = Pragmatic.Resilience.Attributes.BackoffStrategy.ExponentialWithJitter)]");

        pipeline.Should().Contain("RandomNumberGenerator.GetInt32(0, 500)", "jitter is the one that randomises");
        pipeline.Should().Contain("500 * (1 << ");
    }
}
