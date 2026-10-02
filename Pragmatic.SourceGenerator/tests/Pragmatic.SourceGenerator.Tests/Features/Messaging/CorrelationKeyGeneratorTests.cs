using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     Runs the full unified generator over saga sources to verify [CorrelationKey]
///     correlation (the alternative to ICorrelatedMessage) and the PRAG0820/0821 diagnostics.
/// </summary>
public class CorrelationKeyGeneratorTests
{
    // Minimal stubs: FeatureDetector probes IMessageBus for HasMessaging; sagas need the
    // attributes + ICorrelatedMessage + ISaga shape referenced by the generated orchestrator.
    private const string SagaStubs = """
        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
            public sealed record MessageContext(string MessageId);
            public interface IMessageHandler<in T>
            {
                int Order => 0;
                System.Threading.Tasks.Task HandleAsync(T message, MessageContext context, System.Threading.CancellationToken ct = default);
            }
        }

        namespace Pragmatic.Messaging.Saga
        {
            public interface ICorrelatedMessage { string CorrelationId { get; } }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            // FeatureDetector probes this FQN for HasMessaging
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SagaAttribute<TState> : System.Attribute where TState : struct, System.Enum { }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class SagaStartAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class InStateAttribute : System.Attribute
            {
                public InStateAttribute(object state) { }
                public object? NextState { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class CorrelationKeyAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void SagaEvent_WithCorrelationKeyProperty_UsesItInsteadOfInterface()
    {
        var source = SagaStubs + """

            namespace TestApp
            {
                public enum OrderState { Placed, Paid }

                public sealed record OrderPlaced(System.Guid OrderId)
                {
                    [Pragmatic.Messaging.Attributes.CorrelationKey]
                    public System.Guid Key => OrderId;
                }

                [Pragmatic.Messaging.Attributes.Saga<OrderState>]
                public sealed partial class OrderSaga
                {
                    [Pragmatic.Messaging.Attributes.SagaStart]
                    [Pragmatic.Messaging.Attributes.InState(OrderState.Placed, NextState = OrderState.Paid)]
                    public void OnPlaced(OrderPlaced @event) { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        generated.Keys.Should().Contain(k => k.Contains("EventHandlers"),
            "generated files: {0}", string.Join("; ", generated.Keys));
        var handlers = generated.First(kv => kv.Key.Contains("EventHandlers")).Value;
        handlers.Should().Contain("message.Key.ToString()");
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0820").Should().BeEmpty();
    }

    [Fact]
    public void SagaEvent_WithoutAnyCorrelation_ReportsPrag0820()
    {
        var source = SagaStubs + """

            namespace TestApp
            {
                public enum OrderState { Placed, Paid }

                public sealed record OrderPlaced(System.Guid OrderId);

                [Pragmatic.Messaging.Attributes.Saga<OrderState>]
                public sealed partial class OrderSaga
                {
                    [Pragmatic.Messaging.Attributes.SagaStart]
                    [Pragmatic.Messaging.Attributes.InState(OrderState.Placed, NextState = OrderState.Paid)]
                    public void OnPlaced(OrderPlaced @event) { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0820").Should().BeTrue();
    }

    [Fact]
    public void SagaEvent_WithMultipleCorrelationKeys_ReportsPrag0821_AndUsesFirstOrdinal()
    {
        var source = SagaStubs + """

            namespace TestApp
            {
                public enum OrderState { Placed, Paid }

                public sealed record OrderPlaced(System.Guid OrderId)
                {
                    [Pragmatic.Messaging.Attributes.CorrelationKey]
                    public string AKey => OrderId.ToString();

                    [Pragmatic.Messaging.Attributes.CorrelationKey]
                    public string BKey => OrderId.ToString();
                }

                [Pragmatic.Messaging.Attributes.Saga<OrderState>]
                public sealed partial class OrderSaga
                {
                    [Pragmatic.Messaging.Attributes.SagaStart]
                    [Pragmatic.Messaging.Attributes.InState(OrderState.Placed, NextState = OrderState.Paid)]
                    public void OnPlaced(OrderPlaced @event) { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0821").Should().BeTrue();
        var handlers = generated.First(kv => kv.Key.Contains("EventHandlers")).Value;
        handlers.Should().Contain("message.AKey"); // first ordinal wins
    }
}
