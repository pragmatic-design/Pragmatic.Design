using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     Verifies the "wrong-shape" messaging diagnostics fire (PRAG0800/0803/0813).
/// </summary>
public class MessagingShapeDiagnosticsTests
{
    // FeatureDetector probes IMessageBus for HasMessaging; the triggers need their attributes +
    // the interfaces the shape checks look for.
    private const string Stubs = """
        namespace Pragmatic.Messaging
        {
            public interface IMessageBus { }
            public sealed record MessageContext(string MessageId);
            public interface IMessageHandler<in T>
            {
                System.Threading.Tasks.Task HandleAsync(T message, MessageContext context, System.Threading.CancellationToken ct = default);
            }
            public delegate System.Threading.Tasks.Task MessageHandlerDelegate();
            public interface IMessageMiddleware
            {
                int Order => 0;
                System.Threading.Tasks.Task InvokeAsync<T>(T message, MessageContext context, MessageHandlerDelegate next, System.Threading.CancellationToken ct = default);
            }
        }

        namespace Pragmatic.Messaging.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageHandlerAttribute : System.Attribute { public int Order { get; set; } }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessageMiddlewareAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class SagaAttribute<TState> : System.Attribute where TState : struct { }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class EnableOutboxAttribute : System.Attribute { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
        }
        """;

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + "\n" + body, []);

    [Fact]
    public void MessageHandler_WithoutInterface_ReportsPRAG0800()
    {
        var result = Run("""
            namespace TestApp
            {
                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class NotAHandler { }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0800").Should().BeTrue();
    }

    [Fact]
    public void MessageHandler_WithInterface_NoPRAG0800()
    {
        var result = Run("""
            namespace TestApp
            {
                public sealed class OrderPlaced { }
                [Pragmatic.Messaging.Attributes.MessageHandler]
                public sealed partial class OrderHandler : Pragmatic.Messaging.IMessageHandler<OrderPlaced>
                {
                    public System.Threading.Tasks.Task HandleAsync(OrderPlaced message, Pragmatic.Messaging.MessageContext context, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0800").Should().BeFalse();
    }

    [Fact]
    public void MessageMiddleware_WithoutInterface_ReportsPRAG0803()
    {
        var result = Run("""
            namespace TestApp
            {
                [Pragmatic.Messaging.Attributes.MessageMiddleware]
                public sealed class NotMiddleware { }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0803").Should().BeTrue();
    }

    [Fact]
    public void Saga_WithNonEnumState_ReportsPRAG0813()
    {
        var result = Run("""
            namespace TestApp
            {
                public struct NotAnEnum { }
                [Pragmatic.Messaging.Attributes.Saga<NotAnEnum>]
                public sealed partial class BadSaga { }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0813").Should().BeTrue();
    }

    // [EnableOutbox] is a boundary-level attribute (mirror of [EnableSagaPersistence]); its
    // diagnostics PRAG0831 ([EnableOutbox] without Messaging.EFCore) and PRAG0833 (conflicting with
    // [EnableEventOutbox]) are emitted by the Persistence DbContext feature and covered by the
    // MessagingOutboxWiring tests. PRAG0830 ("must be a DbContext") is retired and not reused.
}
