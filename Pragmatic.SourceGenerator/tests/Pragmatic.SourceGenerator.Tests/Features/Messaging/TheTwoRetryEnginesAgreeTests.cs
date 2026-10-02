using Pragmatic.Jobs;
using Pragmatic.Resilience.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     One declaration, one curve: the job engine and the generated message pipeline wait the same.
/// </summary>
/// <remarks>
///     <para>
///         If one engine counted attempts from one and the other from zero, the same
///         <c>[Retry(Exponential)]</c> would produce two sequences a doubling apart. A shared declaration
///         that means two things is the confusion a single declaration exists to remove.
///     </para>
///     <para>
///         The curve is the ordinary one, <c>base × 2^(n-1)</c>, and the ceiling applies to both.
///         The job side is executed; the message side is the generated expression, because the
///         arithmetic is inlined into the handler's pipeline and there is no method to call.
///     </para>
/// </remarks>
public class TheTwoRetryEnginesAgreeTests
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
    ///     The first retry waits the base delay on both sides, and the second waits twice it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The first retry is where counting attempts from zero instead of one shows, and it is
    ///     the one almost every failure ever reaches: a handler that fails twice is rare, a handler that fails once is Tuesday.
    /// </remarks>
    [Fact]
    public void OneDeclaration_OneCurve_AtTheFirstRetryAndTheSecond()
    {
        var job = new JobRetryPolicy(3, TimeSpan.FromMilliseconds(100), BackoffStrategy.Exponential);

        job.ComputeDelay(1).Should().Be(TimeSpan.FromMilliseconds(100),
            "the first retry waits the base delay, which is what base * 2^0 means");
        job.ComputeDelay(2).Should().Be(TimeSpan.FromMilliseconds(200));
        job.ComputeDelay(3).Should().Be(TimeSpan.FromMilliseconds(400));

        var pipeline = PipelineFor(
            "[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 3, BaseDelayMs = 100, Strategy = Pragmatic.Resilience.Attributes.BackoffStrategy.Exponential)]");

        // The loop's first failure is __attempt 0, so `100 * (1 << 0)` is the same 100 ms.
        pipeline.Should().Contain("for (var __attempt = 0;");
        pipeline.Should().Contain("100 * (1 << ");
    }

    /// <summary>Neither engine grows past the ceiling.</summary>
    /// <remarks>
    ///     ⚠️ Saturating where the arithmetic would overflow is not a ceiling: without one, a handler
    ///     declaring twenty attempts with a one-second base waits days. An engine that truncates while
    ///     the other does not is the second half of the disagreement.
    /// </remarks>
    [Fact]
    public void ALongFailingRetry_StopsAtTheCeiling_OnBothSides()
    {
        var job = new JobRetryPolicy(40, TimeSpan.FromSeconds(1), BackoffStrategy.Exponential);

        job.ComputeDelay(30).Should().Be(TimeSpan.FromMinutes(30),
            "the job engine truncates rather than growing");

        PipelineFor(
                "[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 40, BaseDelayMs = 1000, Strategy = Pragmatic.Resilience.Attributes.BackoffStrategy.Exponential)]")
            .Should().Contain(RetryDelayLimits.MaxDelayMilliseconds.ToString(),
                "and so does the generated pipeline, at the same number");
    }

    /// <summary>
    ///     The control: on <c>Fixed</c> nothing moved, because there was never an exponent to be off by.
    /// </summary>
    /// <remarks>
    ///     Without it, "the curves agree" is satisfied by two engines that were both changed into
    ///     something new, and the one strategy that was already correct would have no guard.
    /// </remarks>
    [Fact]
    public void OnFixed_TheTwoEnginesAgree()
    {
        var job = new JobRetryPolicy(3, TimeSpan.FromMilliseconds(100), BackoffStrategy.Fixed);

        job.ComputeDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));
        job.ComputeDelay(5).Should().Be(TimeSpan.FromMilliseconds(100));

        PipelineFor(
                "[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 3, BaseDelayMs = 100, Strategy = Pragmatic.Resilience.Attributes.BackoffStrategy.Fixed)]")
            .Should().Contain("var __delay = 100;");
    }

    /// <summary>
    ///     The second control: a high attempt count still cannot shift the delay negative.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The ceiling is applied after the shift, so it does not by itself prevent the overflow —
    ///     <c>Math.Min</c> of a negative number is still negative. The clamp on the exponent is what
    ///     does, and it has to survive the change that introduced the ceiling.
    /// </remarks>
    [Fact]
    public void AHighAttemptCount_CannotShiftTheDelayNegative()
    {
        var pipeline = PipelineFor(
            "[Pragmatic.Resilience.Attributes.Retry(MaxAttempts = 40, BaseDelayMs = 1000, Strategy = Pragmatic.Resilience.Attributes.BackoffStrategy.Exponential)]");

        pipeline.Should().NotContain("1000 * (1 << __attempt)",
            "unclamped, the shift overflows long before the fortieth attempt");
        pipeline.Should().Contain("Math.Min(__attempt,");
    }
}
