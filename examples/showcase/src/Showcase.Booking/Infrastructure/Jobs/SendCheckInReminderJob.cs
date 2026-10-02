using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Notifications;
using Pragmatic.Resilience.Attributes;

namespace Showcase.Booking.Infrastructure.Jobs;

/// <summary>
///     Delayed job: sends a check-in reminder to the guest 24h before check-in.
///     Scheduled programmatically when a reservation is confirmed.
///     Demonstrates: Pragmatic.Jobs + Pragmatic.Notifications + templates.
/// </summary>
/// <param name="ReservationId">The reservation the reminder is about.</param>
/// <param name="GuestId">The guest it is sent to.</param>
/// <param name="GuestEmail">Where it is sent.</param>
/// <param name="CheckIn">When the guest arrives.</param>
/// <param name="Language">
///     The guest's language, captured when the job is scheduled: a job has no request to take one from,
///     and reading the ambient culture here would send every reminder in whatever language the worker
///     thread last held.
/// </param>
public record CheckInReminderParams(
    Guid ReservationId, Guid GuestId, string GuestEmail, DateTimeOffset CheckIn, string Language = "en");

[Job]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
public sealed partial class SendCheckInReminderJob(
    INotificationService notificationService,
    IPdxTemplates templates) : IJob<CheckInReminderParams>
{
    public const string Template = "check-in-reminder.pdxemail";

    public async Task ExecuteAsync(CheckInReminderParams p, JobContext context, CancellationToken ct)
    {
        var data = new TemplateDataContext()
            .AddSource("reservation", new Dictionary<string, object?>
            {
                ["id"] = p.ReservationId,
                ["checkIn"] = p.CheckIn,
            });

        var mail = await templates.EmailAsync(Template, p.Language, data, ct).ConfigureAwait(false);

        var request = new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct(p.GuestEmail),
            Content = new NotificationContent
            {
                Subject = mail.Subject,
                Body = mail.Text,
                HtmlBody = mail.Html,
            },
            Priority = NotificationPriority.High,
            Category = "transactional",
        };

        await notificationService.SendAsync(request, ct).ConfigureAwait(false);
    }
}
