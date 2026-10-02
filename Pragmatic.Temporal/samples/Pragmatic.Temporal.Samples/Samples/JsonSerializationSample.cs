using System.Text.Json;
using Pragmatic.Temporal.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

public static class JsonSerializationSample
{
    public static void Run()
    {
        Console.WriteLine("--- JSON Serialization Sample ---\n");

        // Configure JSON options with Pragmatic.Temporal converters
        var options = JsonSerializerOptionsExtensions.CreateTemporalOptions();
        options.WriteIndented = true;

        // Sample DTO with temporal types
        var order = new OrderDto
        {
            Id = 1,
            OrderDate = new LocalDate(2024, 6, 15),
            PreferredDeliveryTime = new LocalTime(14, 30),
            CreatedAt = new LocalDateTime(2024, 6, 15, 10, 0),
            DeliverySlot = ZonedDateTime.FromUtc(
                new DateTimeOffset(2024, 6, 16, 12, 0, 0, TimeSpan.Zero),
                "Europe/Rome"),
            EstimatedDuration = Duration.FromHours(2),
            ReminderSchedule = CronExpression.Daily(new TimeOnly(9, 0)),
            // New types: DateRange and Period
            ValidityPeriod = new DateRange(new LocalDate(2024, 6, 15), new LocalDate(2024, 12, 31)),
            SubscriptionLength = new Period(1, 0, 0) // 1 year subscription
        };

        // Serialize
        var json = JsonSerializer.Serialize(order, options);
        Console.WriteLine("Serialized order:");
        Console.WriteLine(json);

        // Deserialize
        var deserialized = JsonSerializer.Deserialize<OrderDto>(json, options)!;
        Console.WriteLine("\nDeserialized order:");
        Console.WriteLine($"  Order date: {deserialized.OrderDate}");
        Console.WriteLine($"  Preferred time: {deserialized.PreferredDeliveryTime}");
        Console.WriteLine($"  Created at: {deserialized.CreatedAt}");
        Console.WriteLine($"  Delivery slot: {deserialized.DeliverySlot}");
        Console.WriteLine($"  Duration: {deserialized.EstimatedDuration}");
        Console.WriteLine($"  Reminder: {deserialized.ReminderSchedule}");
        Console.WriteLine($"  Validity period: {deserialized.ValidityPeriod}");
        Console.WriteLine($"  Subscription: {deserialized.SubscriptionLength} ({deserialized.SubscriptionLength.ToDisplayString()})");

        // ISO 8601 formats
        Console.WriteLine("\n--- ISO 8601 Formats ---");
        Console.WriteLine($"LocalDate: {new LocalDate(2024, 1, 15)} (yyyy-MM-dd)");
        Console.WriteLine($"LocalTime: {new LocalTime(14, 30)} (HH:mm:ss)");
        Console.WriteLine($"LocalDateTime: {new LocalDateTime(2024, 1, 15, 14, 30)} (yyyy-MM-ddTHH:mm:ss)");
        Console.WriteLine($"Duration: {Duration.FromHours(2.5)} (ISO 8601 duration)");
        Console.WriteLine($"Period: {new Period(1, 6, 15)} (P1Y6M15D - ISO 8601 period)");
        Console.WriteLine($"DateRange: {new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31))} (ISO 8601 interval)");

        Console.WriteLine();
    }

    private record OrderDto
    {
        public int Id { get; init; }
        public LocalDate OrderDate { get; init; }
        public LocalTime PreferredDeliveryTime { get; init; }
        public LocalDateTime CreatedAt { get; init; }
        public ZonedDateTime DeliverySlot { get; init; }
        public Duration EstimatedDuration { get; init; }
        public CronExpression? ReminderSchedule { get; init; }
        public DateRange ValidityPeriod { get; init; }
        public Period SubscriptionLength { get; init; }
    }
}