namespace Pragmatic.Resilience.Samples.Samples;

/// <summary>
///     Demonstrates the typed error records for resilience failures.
///     Each error maps to a specific HTTP status code and extends <c>Pragmatic.Result.Error</c>.
/// </summary>
public static class ErrorTypesSample
{
    public static void Show()
    {
        Console.WriteLine("═══ 4. Error Types ═══");
        Console.WriteLine();
        Console.WriteLine("  Pragmatic.Resilience provides typed error records that extend Pragmatic.Result.Error:");
        Console.WriteLine();

        // Show each error type with its HTTP mapping
        var errors = new (string Type, int Code, string Description)[]
        {
            ("TimeoutError", 504, "Operation exceeded timeout"),
            ("RetryExhaustedError", 503, "All retry attempts failed"),
            ("CircuitBrokenError", 503, "Circuit breaker is open"),
            ("BulkheadRejectedError", 429, "Concurrency limit reached")
        };

        foreach (var (type, code, desc) in errors)
            Console.WriteLine($"    {type,-25} (HTTP {code}) — {desc}");

        Console.WriteLine();
        Console.WriteLine("  These integrate with Pragmatic.Result and endpoint error handling:");
        Console.WriteLine("    Result<OrderDto, TimeoutError | PaymentError> → HTTP 504 or 400");
        Console.WriteLine();
    }
}
