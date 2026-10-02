using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Samples.Samples;

/// <summary>
///     Demonstrates type-based routing: the bus dispatches each message to the
///     handler registered for its type. Two handlers registered side-by-side
///     never see each other's messages.
/// </summary>
public static class MultipleMessageTypesSample
{
    public record GuestRegistered(Guid GuestId, string Email);
    public record BookingConfirmed(Guid BookingId, Guid GuestId, DateOnly CheckIn);

    public sealed class GuestRegisteredHandler : IMessageHandler<GuestRegistered>
    {
        public List<GuestRegistered> Received { get; } = [];
        public Task HandleAsync(GuestRegistered message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    public sealed class BookingConfirmedHandler : IMessageHandler<BookingConfirmed>
    {
        public List<BookingConfirmed> Received { get; } = [];
        public Task HandleAsync(BookingConfirmed message, MessageContext context, CancellationToken ct)
        {
            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    public static async Task Run()
    {
        Console.WriteLine("--- Multiple message types (type-based routing) ---");

        var guestHandler = new GuestRegisteredHandler();
        var bookingHandler = new BookingConfirmedHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();
        services.AddSingleton<IMessageHandler<GuestRegistered>>(guestHandler);
        services.AddSingleton<IMessageHandler<BookingConfirmed>>(bookingHandler);

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        var guestId = Guid.NewGuid();
        await bus.PublishAsync(new GuestRegistered(guestId, "alice@example.com"));
        await bus.PublishAsync(new BookingConfirmed(Guid.NewGuid(), guestId, new DateOnly(2026, 5, 14)));
        await bus.PublishAsync(new BookingConfirmed(Guid.NewGuid(), guestId, new DateOnly(2026, 7, 30)));

        Console.WriteLine($"  GuestRegistered handler   : {guestHandler.Received.Count} received");
        Console.WriteLine($"  BookingConfirmed handler  : {bookingHandler.Received.Count} received");
        Console.WriteLine($"  cross-contamination       : " +
            (guestHandler.Received.Count == 1 && bookingHandler.Received.Count == 2
                ? "none — each message reached its own handler"
                : "DETECTED — routing is broken"));
        Console.WriteLine();
    }
}
