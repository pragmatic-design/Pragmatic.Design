using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Notifications;

namespace Showcase.Booking.Infrastructure.Notifications;

/// <summary>
///     Sends the guest a confirmation when a reservation is confirmed — from
///     <c>templates/reservation-confirmed.pdxemail</c>, in the guest's own language.
/// </summary>
/// <remarks>
///     <para>
///         Uses [EventHandler] (intra-boundary) — the cross-boundary invoice creation is handled separately
///         by Billing's [MessageHandler]. Demonstrates Pragmatic.Events + Notifications + templates.
///     </para>
///     <para>
///         ⚠️ The language is the <b>guest's</b>, not the request's: the member of staff who confirms a
///         booking may work in English while the guest reads Italian. The template owns the wording, the
///         subject included; this class decides only the language and the values the template may name.
///     </para>
/// </remarks>
[EventHandler]
public sealed partial class ReservationConfirmedNotificationHandler(
    INotificationService notificationService,
    IReadRepository<Guest> guests,
    IPdxTemplates templates) : IDomainEventHandler<ReservationConfirmed>
{
    public const string Template = "reservation-confirmed.pdxemail";

    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct = default)
    {
        var guest = await guests.GetByIdAsync(@event.GuestId, ct).ConfigureAwait(false);

        var data = new TemplateDataContext()
            .AddSource("guest", new Dictionary<string, object?>
            {
                ["name"] = guest is null ? "" : $"{guest.FirstName} {guest.LastName}",
            })
            .AddSource("reservation", new Dictionary<string, object?>
            {
                ["id"] = @event.ReservationId,
                ["checkIn"] = @event.CheckIn,
                ["checkOut"] = @event.CheckOut,
                ["total"] = Money.From(@event.TotalAmount, @event.Currency),
            });

        var mail = await templates
            .EmailAsync(Template, guest?.PreferredLanguage ?? "en", data, ct)
            .ConfigureAwait(false);

        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User(@event.GuestId.ToString()),
            Content = new NotificationContent
            {
                Subject = mail.Subject,
                Body = mail.Text,
                HtmlBody = mail.Html,
            },
            Category = "transactional",
        };

        await notificationService.EnqueueAsync(request, ct).ConfigureAwait(false);
    }
}
