using Microsoft.Extensions.Logging;
using Pragmatic.Audit;

namespace Pragmatic.Messaging.Auditing;

/// <summary>
///     Records an audit entry for every handled message.
///     Order -100: runs before retry middleware, so every attempt is captured.
/// </summary>
/// <remarks>
///     <para>
///         Writes to the framework's one audit trail rather than a trail of its own. The messaging
///         trail it replaces could not be verified and carried <c>PayloadJson</c> — the serialized
///         message, personal data included — which is the single defect that motivated a shared trail
///         with no free-form payload field.
///     </para>
///     <para>
///         <b>The actor is taken from the context, not resolved here.</b> An audit entry identifies
///         people by pseudonym, and translating an identity into one at this point would mean a
///         database lookup inside the message pipeline, on every message, plus a dependency from
///         messaging onto the privacy module. The identity is pseudonymised where it enters the system;
///         by the time a message is being handled, <see cref="MessageContext.UserId" /> already carries
///         a reference rather than a name.
///     </para>
/// </remarks>
public sealed partial class AuditMiddleware(
    IAuditTrail trail,
    ILogger<AuditMiddleware> logger)
    : IMessageMiddleware
{
    /// <summary>Runs very early in the pipeline, before retry.</summary>
    public int Order => -100;

    /// <inheritdoc />
    public async Task InvokeAsync<T>(
        T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
        where T : notnull
    {
        var messageType = typeof(T).FullName ?? typeof(T).Name;
        var outcome = AuditOutcome.Success;
        string? detail = null;

        try
        {
            await next().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            outcome = AuditOutcome.Failed;
            detail = Describe(ex);
            throw;
        }
        finally
        {
            // An audit write must not mask a handler exception. A throwing finally replaces the
            // in-flight exception per the C# spec, hiding the original failure from callers and logs.
            try
            {
                await trail.RecordAsync(
                    new AuditEntry
                    {
                        SegmentId = string.Empty,       // assigned by the trail
                        OccurredAt = default,           // the trail stamps it
                        Category = AuditCategory.Message,
                        Operation = outcome == AuditOutcome.Success
                            ? "Messaging.MessageHandled"
                            : "Messaging.MessageFailed",
                        ActorRef = context.UserId,
                        TenantId = context.TenantId,
                        CorrelationId = context.CorrelationId,
                        TargetType = messageType,
                        TargetId = context.MessageId,
                        Outcome = outcome,
                        Detail = detail,
                    },
                    ct).ConfigureAwait(false);

                LogAuditRecorded(outcome.ToString(), messageType, context.MessageId);
            }
            catch (Exception auditEx)
            {
                LogAuditFailed(messageType, context.MessageId, auditEx);
            }
        }
    }

    /// <summary>
    ///     The exception type plus the first line of its message, capped.
    /// </summary>
    /// <remarks>
    ///     The full message routinely echoes payload values that field-level redaction never sees, and
    ///     a stack dump does not belong in an audit trail. What survives this still passes through the
    ///     trail's own redactor on the way in.
    /// </remarks>
    private static string Describe(Exception ex)
    {
        const int maxLineLength = 200;
        var message = ex.Message ?? string.Empty;

        var breakIndex = message.AsSpan().IndexOfAny('\r', '\n');
        var firstLine = breakIndex >= 0 ? message[..breakIndex] : message;
        if (firstLine.Length > maxLineLength)
            firstLine = firstLine[..maxLineLength];

        return firstLine.Length == 0 ? ex.GetType().Name : $"{ex.GetType().Name}: {firstLine}";
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Audit: {Outcome} {MessageType} (id: {MessageId})")]
    private partial void LogAuditRecorded(string outcome, string messageType, string messageId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Audit trail failed to record {MessageType} (id: {MessageId}) — handler result preserved")]
    private partial void LogAuditFailed(string messageType, string messageId, Exception exception);
}
