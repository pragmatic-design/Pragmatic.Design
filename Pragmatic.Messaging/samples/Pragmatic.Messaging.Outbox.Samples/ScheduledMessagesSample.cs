using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Scheduled (delayed) delivery via the <see cref="IMessageScheduler" />
///     contract. The core package ships the abstraction
///     (<c>ScheduleAsync(message, delay)</c> / <c>ScheduleAsync(message, deliveryTime)</c>
///     / <c>CancelAsync</c>); a concrete scheduler is provided by an infrastructure
///     package (e.g. Pragmatic.Messaging.Jobs for durable scheduling). This sample
///     supplies a minimal timer-backed scheduler so the contract runs end-to-end
///     in-process without external infrastructure: it defers the publish with
///     <c>Task.Delay</c> and routes the message back through <see cref="IMessageBus" />.
/// </summary>
public static class ScheduledMessagesSample
{
    public sealed record CheckInReminder(Guid BookingId);

    public sealed class ReminderHandler : IMessageHandler<CheckInReminder>
    {
        private readonly TaskCompletionSource _fired = new();
        public Task Fired => _fired.Task;
        public DateTimeOffset? FiredAt { get; private set; }

        public Task HandleAsync(CheckInReminder message, MessageContext context, CancellationToken ct)
        {
            FiredAt = DateTimeOffset.UtcNow;
            _fired.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Minimal in-process <see cref="IMessageScheduler" />: a timer-backed
    ///     scheduler that publishes through the bus after the delay. Not durable
    ///     across restarts — for that use a persistent scheduler implementation.
    /// </summary>
    public sealed class TimerMessageScheduler(IMessageBus bus) : IMessageScheduler
    {
        public Task<Guid> ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken ct = default) where T : notnull
        {
            var id = Guid.NewGuid();
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay, ct);
                await bus.PublishAsync(message, ct);
            }, ct);
            return Task.FromResult(id);
        }

        public Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset scheduledAt, CancellationToken ct = default) where T : notnull
            => ScheduleAsync(message, scheduledAt - DateTimeOffset.UtcNow, ct);

        public Task CancelAsync(Guid scheduleId, CancellationToken ct = default) => Task.CompletedTask;
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Scheduled messages (delayed delivery) ---");

        var handler = new ReminderHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IMessageScheduler, TimerMessageScheduler>();
        services.AddSingleton<IMessageHandler<CheckInReminder>>(handler);

        await using var sp = services.BuildServiceProvider();
        var scheduler = sp.GetRequiredService<IMessageScheduler>();

        var scheduledAt = DateTimeOffset.UtcNow;
        var delay = TimeSpan.FromMilliseconds(300);
        await scheduler.ScheduleAsync(new CheckInReminder(Guid.NewGuid()), delay);
        Console.WriteLine($"  scheduled with delay     : {delay.TotalMilliseconds:F0} ms");

        // Wait (with a margin) for the timer to elapse and the bus to deliver.
        await Task.WhenAny(handler.Fired, Task.Delay(TimeSpan.FromSeconds(5)));

        var elapsed = (handler.FiredAt ?? DateTimeOffset.UtcNow) - scheduledAt;
        Console.WriteLine($"  reminder delivered       : {handler.FiredAt is not null}");
        Console.WriteLine($"  elapsed before delivery  : {elapsed.TotalMilliseconds:F0} ms (>= delay)");
        Console.WriteLine();
    }
}
