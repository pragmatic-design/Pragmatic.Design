using Casework.Intake.Events;
using Casework.Intake.Infrastructure.Letters;
using Casework.Intake.Infrastructure.Mail;
using Casework.Intake.Letters;
using Microsoft.Extensions.Logging;
using Pragmatic.Documents.Pdf;
using Pragmatic.Email;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.MultiTenancy;
using Pragmatic.Storage;

namespace Casework.Intake.Infrastructure.EventHandlers;

/// <summary>
///     A case has been decided: render the letter, keep it, and mail it to the applicant.
/// </summary>
/// <remarks>
///     <para>
///         <b>In this order, and the order is the point.</b> The letter is rendered once and
///         <b>stored</b>; the mail attaches the bytes that were stored. A mail that rendered its own
///         copy would give the applicant one document while the organisation kept another, with nothing
///         to say which of the two was sent.
///     </para>
///     <para>
///         ⚠️ <b>After the commit, deliberately.</b> The event is captured into the outbox inside the
///         decision's transaction and delivered afterwards, so an applicant is never told about a
///         decision a failed commit rolled back. The other order — mail inside the transaction — is the
///         one that sends letters about things that did not happen.
///     </para>
///     <para>
///         ⚠️ <b>A <c>[MessageHandler]</c> and not an <c>[EventHandler]</c>, and it is not a preference.</b>
///         This boundary has <c>[EnableOutbox]</c>, and <c>OutboxInterceptor</c> calls
///         <c>ClearDomainEvents()</c> while the save is in flight — so by the time
///         <c>EfCoreUnitOfWork</c> takes the entity's events to dispatch them, there are none, and every
///         <c>IDomainEventHandler&lt;T&gt;</c> of the boundary is registered and never called. The first
///         version of this class was one, and nothing said it would not run — now the build does,
///         <c>PRAG0837</c>, naming this attribute. What does arrive is the message the pump
///         publishes from the outbox row, which is this.
///     </para>
///     <para>
///         ⚠️ <b>An organisation with no sender address sends nothing</b>, and this says so in the log
///         rather than falling back to the application's own address. That fallback is the one that
///         mails every organisation's applicants from one place and invites replies to the wrong people.
///     </para>
/// </remarks>
[MessageHandler]
internal sealed partial class TellTheApplicantTheDecision(
    IWriteTheDecisionLetter letters,
    ITellTheApplicant mail,
    IFileStorage files,
    IEmailSender post,
    ITenantContext organisation,
    ILogger<TellTheApplicantTheDecision> logger) : IMessageHandler<CaseDecided>
{
    public async Task HandleAsync(
        CaseDecided @event, MessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var letter = await letters.ForCaseAsync(@event.CaseId, ct).ConfigureAwait(false);

        // What the template asked for and did not get. It does not stop the letter — an organisation's
        // template is its own business — but it is not allowed to be invisible either.
        foreach (var warning in letter.Warnings)
            LogTemplateGap(@event.Number, warning.Path, warning.Message);

        var pdf = await PdfRenderer.RenderAsync(letter.Model, ct: ct).ConfigureAwait(false);

        // Stored first, so what is attached below is a file that exists and can be fetched again.
        using var bytes = new MemoryStream(pdf);
        var stored = await files
            .SaveAsync(
                bytes, $"{@event.Number}.pdf",
                LetterMarkup.ContainerFor(organisation.TenantId ?? ""), ct)
            .ConfigureAwait(false);

        var readBack = await files.GetAsync(stored, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The decision letter of case {@event.Number} was stored at {stored} and is not there.");

        byte[] attachment;
        await using (readBack.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await readBack.CopyToAsync(buffer, ct).ConfigureAwait(false);
            attachment = buffer.ToArray();
        }

        var composed = await mail.AboutTheDecisionAsync(@event.CaseId, attachment, ct).ConfigureAwait(false);
        if (composed is null)
        {
            LogNobodyToWriteTo(@event.Number);

            return;
        }

        foreach (var warning in composed.Warnings)
            LogTemplateGap(@event.Number, warning.Path, warning.Message);

        await post.SendAsync(composed.Message, ct).ConfigureAwait(false);

        LogSent(@event.Number, composed.FromTheOrganisationsOwnTemplate);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The decision letter of case {Number} asked for {Path}, which nobody provided: {Detail}")]
    private partial void LogTemplateGap(string number, string path, string detail);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Case {Number} was decided and nobody was written to: the applicant has no address, or this organisation has no sender address.")]
    private partial void LogNobodyToWriteTo(string number);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The decision of case {Number} was mailed (the organisation's own template: {ItsOwn}).")]
    private partial void LogSent(string number, bool itsOwn);
}
