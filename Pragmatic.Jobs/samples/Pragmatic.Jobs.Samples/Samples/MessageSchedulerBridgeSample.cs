using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Jobs;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     The <b>Messaging.Jobs bridge</b>: <see cref="IMessageScheduler"/> lets you schedule a
///     <i>message</i> (not a job) for future delivery, backed by <see cref="IJobScheduler"/>.
///     <see cref="MessagingJobsExtensions.EnableScheduledMessages"/> registers
///     <see cref="JobsMessageScheduler"/> as the <see cref="IMessageScheduler"/>; under the
///     hood it serializes the message and enqueues a <c>PublishMessageJob</c> via the same
///     job store / processor shown in the other samples.
///     <para>
///         <b>Setup-only demo.</b> A live round-trip would also require the full
///         Pragmatic.Messaging runtime (<c>IMessageBus</c>, <c>IMessageTypeRegistry</c>,
///         transports) to actually publish on delivery — out of scope for the Jobs samples
///         project. This sample shows the exact wiring and the scheduler API surface,
///         compiled against the real bridge types. For a runnable end-to-end version see
///         <c>Pragmatic.Messaging/samples/Pragmatic.Messaging.Outbox.Samples/ScheduledMessagesSample.cs</c>.
///     </para>
/// </summary>
public static class MessageSchedulerBridgeSample
{
    /// <summary>A domain message you might want to publish later.</summary>
    public sealed record ReminderDue(Guid ReservationId, string GuestEmail);

    public static Task Run()
    {
        Console.WriteLine("--- Messaging.Jobs bridge (IMessageScheduler) ---");

        // Wiring: EnableScheduledMessages() on the MessagingBuilder registers the
        // Jobs-backed IMessageScheduler. In a real host this sits next to your
        // AddPragmaticJobs() registration so the PublishMessageJob can be processed.
        var services = new ServiceCollection();
        services.AddPragmaticMessaging(messaging => messaging.EnableScheduledMessages());

        var schedulerRegistered = services.Any(d => d.ServiceType == typeof(IMessageScheduler));
        Console.WriteLine($"  EnableScheduledMessages() wired IMessageScheduler : {schedulerRegistered}");
        Console.WriteLine($"  IMessageScheduler impl   : {nameof(JobsMessageScheduler)}");

        // The scheduler API (shown for documentation — resolving + invoking it needs the
        // full Messaging runtime, so we only demonstrate the call shapes here):
        //
        //   var scheduler = provider.GetRequiredService<IMessageScheduler>();
        //   Guid id = await scheduler.ScheduleAsync(new ReminderDue(resId, email), TimeSpan.FromHours(24));
        //   Guid id = await scheduler.ScheduleAsync(new ReminderDue(resId, email), DateTimeOffset.UtcNow.AddDays(1));
        //   await scheduler.CancelAsync(id);   // delegates to IJobScheduler.CancelAsync

        Console.WriteLine("  ScheduleAsync(message, delay)        -> ScheduleAsync<PublishMessageJob, ...>");
        Console.WriteLine("  ScheduleAsync(message, scheduledAt)  -> ScheduleAtAsync<PublishMessageJob, ...>");
        Console.WriteLine("  CancelAsync(id)                      -> IJobScheduler.CancelAsync(id)");
        Console.WriteLine();

        return Task.CompletedTask;
    }
}
