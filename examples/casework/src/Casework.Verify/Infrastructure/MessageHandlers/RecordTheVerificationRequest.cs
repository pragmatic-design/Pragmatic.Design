using Casework.Intake.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;

namespace Casework.Verify.Infrastructure.MessageHandlers;

/// <summary>
///     Intake asked for a verification: this service writes it down, in its own database.
/// </summary>
/// <remarks>
///     <para>
///         A message leaves one process and this handler runs in another. What it does is deliberately
///         the smallest true thing — it records the request; the verification's own states, its outcome
///         and the answer going back belong to <c>AnswerVerificationAction</c>.
///     </para>
///     <para>
///         It writes through the boundary's <b>internal</b> interface and not through a repository, which
///         is the framework's shape and not a preference: the operation's invoker owns the transaction, so
///         this handler has no unit of work to remember to commit. The internal one specifically, because
///         a message handler arrives with <b>no principal</b> — that interface is the one that runs as an
///         internal call, and the public one would enforce a permission nobody is there to hold.
///     </para>
///     <para>
///         The group's own internal interface, injected directly — which is what a handler reading the
///         generated documentation reaches for. It is registered in the container, so there is no need
///         to go through <c>IVerifyInternalActions.Verifications</c> to reach it.
///     </para>
///     <para>
///         ⚠️ It is the same object either way, and that is the part worth knowing: the container
///         resolves both to one scoped implementation. What it is <em>not</em> is the group's public
///         interface, which answers with the guarded twin and would enforce a permission nobody here
///         holds — on a group, spec 7.25 draws the distinction by which <em>instance</em> answers, not
///         by which interface is injected.
///     </para>
///     <para>
///         ⚠️ <b>The queue is named after this module, not after this handler</b> — it is
///         <c>verify.verification-requested</c>, and Intake's saga subscribing to the same event gets
///         <c>intake.verification-requested</c>, which is what makes each service receive a copy. A name
///         from the transport and the message type alone (<c>rabbitmq-VerificationRequested</c>) would
///         have the two services declare one queue on one broker and divide the requests between them,
///         and Intake's saga would never start. Not per handler, because
///         one subscription serves every handler of a type in this process; per <em>module</em>.
///     </para>
///     <para>
///         The tenant is not in the message and does not need to be: the transport carries it in a header
///         and the consumer restores it into this scope before the handler runs, so the row written below
///         belongs to the organisation whose case asked. Nothing here does that, and nothing here should.
///     </para>
///     <para>
///         ⚠️ <b>And a message that arrives with no tenant is refused by the framework, not here.</b>
///         The query filter is fail-closed on reads, and on writes <c>TenantInterceptor</c> refuses the
///         write when <c>MultiTenancyOptions.RequireTenant</c> is on, which is its default. Without that
///         the row would be written onto the <b>shared</b> database with an empty tenant and be invisible
///         afterwards to this service's own API — so the handler needs no tenant check of its own.
///     </para>
/// </remarks>
[MessageHandler]
internal sealed partial class RecordTheVerificationRequest(IVerifyVerificationsInternalActions verifications)
    : IMessageHandler<VerificationRequested>
{
    public async Task HandleAsync(
        VerificationRequested message, MessageContext context, CancellationToken ct = default)
    {
        var recorded = await verifications
            .RecordVerificationRequest(
                caseId: message.CaseId, kind: message.Kind, deadline: message.Deadline, ct: ct)
            .ConfigureAwait(false);

        // ⚠️ Awaiting the call and looking at nothing would acknowledge a refused write — a validation
        // rule, a tenant the interceptor will not accept — and the request this service exists to
        // record would be gone with nothing to find. The nack sends it to the dead letter
        // instead, which is the one place a request nobody could write is still visible.
        if (recorded.IsFailure)
            throw new InvalidOperationException(
                $"The verification asked for case {message.CaseId} could not be recorded: {recorded.Error}");
    }
}
