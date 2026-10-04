using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Messaging.Templates;

internal sealed partial class SagaOrchestratorTemplate
{
    /// <summary>How many times one event is tried when its save keeps losing to another writer.</summary>
    private const int ConflictAttempts = 5;

    /// <summary>
    ///     Emits <c>HandleEventAsync</c>: one attempt of <c>HandleEventOnceAsync</c>, again from a fresh read
    ///     when the save lost to another writer, a bounded number of times.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two messages for one saga, handled at once, both read the same version; the second to
    ///         save loses (<c>SagaConcurrencyException</c>). Taking the path of any failure — the saga marked
    ///         Faulted, the exception rethrown — a transport that does not redeliver, RabbitMQ without a
    ///         dead-letter exchange, would discard the message, and a compensation waiting for that
    ///         acknowledgement would wait for nothing.
    ///     </para>
    ///     <para>
    ///         Tried again here and not left to the transport, so the outcome does not depend on which one is
    ///         configured. Each attempt reads the saga again, so the step runs against the state the other
    ///         writer left — which may route it to a different step, or to none. After the last attempt the
    ///         conflict surfaces as it is, still without marking the saga Faulted: nothing about it is wrong.
    ///     </para>
    /// </remarks>
    private void RenderHandleEventWithRetry()
    {
        XmlSummary("Routes an event to the correct saga step based on current state, reading the saga again when its save loses to another writer.");
        Method("HandleEventAsync", () =>
        {
            AppendLine("for (var attempt = 1; ; attempt++)");
            Block(() =>
            {
                AppendLine("try");
                Block(() =>
                {
                    AppendLine("await HandleEventOnceAsync(@event, eventType, correlationId, ct).ConfigureAwait(false);");
                    AppendLine("return;");
                });
                AppendLine($"catch (global::Pragmatic.Messaging.Saga.SagaConcurrencyException) when (attempt < {ConflictAttempts})");
                Block(() => AppendLine($"LogSagaConflictRetrying(\"{_model.TypeName}\", correlationId, attempt);"));
            });
        },
        "global::System.Threading.Tasks.Task",
        new System.Collections.Generic.List<MethodParameter>
        {
            new() { Type = "object", Name = "@event" },
            new() { Type = "global::System.Type", Name = "eventType" },
            new() { Type = "string", Name = "correlationId" },
            new() { Type = "CancellationToken", Name = "ct", DefaultValue = "default" },
        },
        modifiers: new MethodModifiers { IsAsync = true });
    }

    private void RenderConflictLoggerMessage()
    {
        RenderLogMethod("LogSagaConflictRetrying", "Information",
            "Saga {SagaType} (correlation {CorrelationId}) was saved by another writer first; reading it again (attempt {Attempt})",
            [("string", "sagaType"), ("string", "correlationId"), ("int", "attempt")],
            ["sagaType", "correlationId", "attempt"]);
    }
}
