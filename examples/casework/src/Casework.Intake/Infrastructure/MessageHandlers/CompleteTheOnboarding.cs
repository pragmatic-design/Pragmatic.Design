using Casework.Verify.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.MultiTenancy;

namespace Casework.Intake.Infrastructure.MessageHandlers;

/// <summary>
///     Verify has made its own database for an organisation: the onboarding is over, and the
///     organisation may work.
/// </summary>
/// <remarks>
///     <para>
///         This is the only place an organisation becomes <c>Active</c> in Intake, and that is the point:
///         "ready" is something the far end reported, not something this service decided
///         when it finished its own half.
///     </para>
///     <para>
///         ⚠️ <b>This service consumes a contract of the other one</b> — <c>Casework.Verify.Contracts</c>.
///         The two contract assemblies are the shape of the
///         dependency: each service publishes what it knows and references the other's types to hear
///         them, and neither references the other's implementation.
///     </para>
///     <para>
///         An organisation this service does not know is <b>not</b> an error: a message can arrive after
///         an operator removed the row, and the id in it is not something to create from. Nothing is
///         written, and the store says so by finding nothing.
///     </para>
/// </remarks>
[MessageHandler]
internal sealed partial class CompleteTheOnboarding(ITenantStore organisations)
    : IMessageHandler<TenantReady>
{
    public async Task HandleAsync(TenantReady message, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (await organisations.GetByIdAsync(message.TenantId, ct).ConfigureAwait(false) is not { } organisation)
            return;

        await organisations.UpdateAsync(organisation with { State = TenantState.Active }, ct)
            .ConfigureAwait(false);
    }
}
