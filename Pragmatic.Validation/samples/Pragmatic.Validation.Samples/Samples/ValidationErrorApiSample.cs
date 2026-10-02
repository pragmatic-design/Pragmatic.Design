using Pragmatic.Validation.Types;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     ValidationError API: building errors fluently, collection expressions,
///     nested paths, combining, and parameterized messages.
/// </summary>
public static class ValidationErrorApiSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. ValidationError API — Building & Combining");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowFluentBuilding();
        ShowCollectionExpression();
        ShowNestedPaths();
        ShowCombining();

        Console.WriteLine();
    }

    private static void ShowFluentBuilding()
    {
        Console.WriteLine("  5.1 Fluent building with WithFor()");
        Console.WriteLine("  -------------------------------------");

        var error = ValidationError.Valid
            .WithFor("Email", "validation.email.required")
            .WithFor("Email", "validation.email.format")
            .WithFor("Name", "validation.name.minlength")
            .WithFor("Age", "validation.age.range");

        Console.WriteLine($"    IsFailure: {error.IsFailure}, Count: {error.Count}");
        foreach (var issue in error.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowCollectionExpression()
    {
        Console.WriteLine("  5.2 Collection expression with parameters");
        Console.WriteLine("  --------------------------------------------");

        ValidationError error =
        [
            new ValidationIssue("validation.required", "ProductId"),
            new ValidationIssue("validation.range", "Quantity", ("min", 1), ("max", 100)),
            new ValidationIssue("validation.maxlength", "Description", ("maxLength", 500))
        ];

        Console.WriteLine($"    Count: {error.Count}");
        foreach (var issue in error.Issues)
        {
            var paramsStr = issue.Parameters is { Count: > 0 }
                ? $" [{string.Join(", ", issue.Parameters.Select(p => $"{p.Key}={p.Value}"))}]"
                : "";
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}{paramsStr}");
        }

        Console.WriteLine();
    }

    private static void ShowNestedPaths()
    {
        Console.WriteLine("  5.3 Nested paths — WithNested() for collections and objects");
        Console.WriteLine("  -------------------------------------------------------------");

        var error = ValidationError.Valid
            // Indexed collection: Items[0].ProductId
            .WithNested("Items", 0, "ProductId", "validation.required")
            .WithNested("Items", 0, "Quantity", "validation.range", ("min", 1))
            .WithNested("Items", 1, "UnitPrice", "validation.positive")
            // Non-indexed nested: Address.Street
            .WithNested("Address", "Street", "validation.required");

        Console.WriteLine($"    Count: {error.Count}");
        foreach (var issue in error.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowCombining()
    {
        Console.WriteLine("  5.4 Combining errors from multiple sources");
        Console.WriteLine("  ---------------------------------------------");

        var syncErrors = ValidationError.For("Email", "validation.required");
        var asyncErrors = ValidationError.For("Email", "validation.email.unique");
        var combined = syncErrors.Combine(asyncErrors);

        Console.WriteLine($"    Sync errors: {syncErrors.Count}");
        Console.WriteLine($"    Async errors: {asyncErrors.Count}");
        Console.WriteLine($"    Combined: {combined.Count}");
        foreach (var issue in combined.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }
}
