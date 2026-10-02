using Casework.Intake.Events;
using Casework.Verify.Events;
using Casework.Verify.Organisations;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.MultiTenancy;
using Pragmatic.Resilience.Attributes;

namespace Casework.Verify.Infrastructure.MessageHandlers;

/// <summary>
///     Intake registered an organisation: this service makes its own database for it and says when it is
///     ready.
/// </summary>
/// <remarks>
///     <para>
///         <b>The second half of an onboarding: two services, two databases.</b>
///         Neither service provisions for the other: what crossed is that an organisation exists and
///         whether it asked for a database of its own. Where that database is, in this service, is built
///         here from <b>this</b> service's template — Intake does not know it and has no business
///         deciding it.
///     </para>
///     <para>
///         ⚠️ <b>The tenant is in the payload, not in the header.</b> Every other handler in this service
///         works inside a tenant the transport restored; this one is about an organisation
///         that does not exist here yet, so there is nothing for the transport to restore and nothing to
///         route: the register is on the shared database, and <c>ITenantStore</c> opens that connection
///         itself.
///     </para>
///     <para>
///         ⚠️ <b>A failure here must be loud.</b> It throws rather than returning, so the message is
///         retried and then dead-lettered: the organisation stays <c>Provisioning</c> in both registers,
///         every request as it is refused with 403, and the failure is in a queue somebody looks at. The
///         alternative — swallowing it — produces the exact defect the two-phase state exists to prevent: an
///         organisation that can open a case and can never have it verified.
///     </para>
///     <para>
///         ⚠️ <b>One at a time, whatever the broker delivers.</b> This handler creates a database and
///         runs a migration on it; ten onboardings arriving together would open ten of those at once, on
///         a server sized for the traffic and not for the burst. <c>[ConcurrencyLimit]</c> is the
///         declaration for that, and the messages that wait are not lost — the consumer holds them.
///     </para>
///     <para>
///         What it buys is proved where it can be seen:
///         <c>Pragmatic.Messaging.Core.Tests.Unit.OneAtATimeIsWhatTheDeclarationBuysTests</c> holds one
///         execution inside the handler and watches the second wait outside, with the control that the
///         same choreography overlaps when nothing is declared. The gate is a static semaphore in the
///         generated pipeline, so from outside the process two messages both handled look the same
///         whether one waited or not — which is why this declaration is not asserted here.
///     </para>
/// </remarks>
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
[ConcurrencyLimit(1)]
internal sealed partial class MakeRoomForANewOrganisation(
    ITenantStore organisations,
    IProvisionTenantDatabases databases,
    IMessageBus bus)
    : IMessageHandler<TenantOnboarded>
{
    public async Task HandleAsync(
        TenantOnboarded message, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrEmpty(message.TenantId))
            throw new InvalidOperationException(
                $"A TenantOnboarded arrived with no tenant id (name '{message.Name}', own database "
                + $"{message.WantsItsOwnDatabase}, at {message.OccurredAt:O}): refusing to guess which "
                + "organisation it is about.");

        // Where its rows will be in **this** service: built from Verify's own template, and nothing in
        // the message names it.
        var connectionString = databases.ConnectionStringFor(message.TenantId, message.WantsItsOwnDatabase);

        // Idempotent from here down, because at-least-once delivery means this can run twice: the
        // register is only written when it is not there, and provisioning an existing database is a
        // no-op that still checks the schema.
        //
        // ⚠️ The row before the database, as in Intake: it is what refuses this organisation on this
        // service's own API meanwhile, and — when the line after it throws — the only trace that
        // anything was attempted here at all.
        if (await organisations.GetByIdAsync(message.TenantId, ct).ConfigureAwait(false) is null)
            await organisations.CreateAsync(
                    new TenantInfo
                    {
                        TenantId = message.TenantId,
                        TenantName = message.Name,
                        ConnectionString = connectionString,
                        State = TenantState.Provisioning,
                        CreatedAt = message.OccurredAt
                    },
                    ct)
                .ConfigureAwait(false);

        if (connectionString is not null)
            await databases.ProvisionAsync(message.TenantId, connectionString, ct).ConfigureAwait(false);

        // Active only now: between the row above and this line, this service refuses the organisation
        // its own API — which is what makes "ready" a thing that was done rather than a thing that was
        // announced.
        await organisations.UpdateAsync(
                new TenantInfo
                {
                    TenantId = message.TenantId,
                    TenantName = message.Name,
                    ConnectionString = connectionString,
                    State = TenantState.Active,
                    CreatedAt = message.OccurredAt
                },
                ct)
            .ConfigureAwait(false);

        await bus.PublishAsync(new TenantReady(message.TenantId, DateTimeOffset.UtcNow), ct)
            .ConfigureAwait(false);
    }
}
