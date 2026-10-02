using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Temporal.Samples.Samples;

public static class ClockSample
{
    public static void Run()
    {
        Console.WriteLine("--- Clock Sample ---\n");

        // Production code uses SystemClock
        var productionClock = SystemClock.Instance;
        Console.WriteLine($"System clock UTC now: {productionClock.UtcNow}");
        Console.WriteLine($"System clock Today: {productionClock.Today}");

        // Test code uses TestClock for deterministic tests
        var testClock = new TestClock();
        testClock.SetDateTime(2024, 3, 31, 1, 30); // Just before DST in Europe

        Console.WriteLine($"\nTest clock UTC now: {testClock.UtcNow}");

        // Advance time in tests
        testClock.AdvanceHours(2);
        Console.WriteLine($"After advancing 2 hours: {testClock.UtcNow}");

        // Auto-advance for async tests
        var asyncClock = new TestClock()
            .SetDateTime(2024, 1, 1, 0, 0)
            .WithAutoAdvance(TimeSpan.FromSeconds(1));

        Console.WriteLine($"\nAuto-advance clock: {asyncClock.UtcNow}");
        Console.WriteLine($"Next read (auto-advanced): {asyncClock.UtcNow}");
        Console.WriteLine($"Next read (auto-advanced): {asyncClock.UtcNow}");

        // DST test helpers
        var dstClock = new TestClock();
        dstClock.SetBeforeRomeSpringForward();
        Console.WriteLine($"\nJust before Rome spring forward: {dstClock.UtcNow}");

        Console.WriteLine();
    }
}