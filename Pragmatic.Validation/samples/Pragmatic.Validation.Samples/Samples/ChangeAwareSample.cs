namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Change-aware validation: Validate(modifiedProperties) only checks the fields
///     that actually changed — used for PATCH/update operations.
/// </summary>
public static class ChangeAwareSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Change-Aware Validation — Validate Only Modified Fields");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowCreateMode();
        ShowUpdateMode();

        Console.WriteLine();
    }

    private static void ShowCreateMode()
    {
        Console.WriteLine("  7.1 Create mode (null) — validate ALL properties");
        Console.WriteLine("  ---------------------------------------------------");

        var request = new CreateUserRequest
        {
            Email = "not-an-email",
            Name = "A",
            Age = 5
        };

        // null = create mode → validate everything
        ISyncValidator validator = request;
        var result = validator.Validate(null);
        Console.WriteLine($"    Validate(null) → validates all: {result.Count} issues");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowUpdateMode()
    {
        Console.WriteLine("  7.2 Update mode — validate only modified properties");
        Console.WriteLine("  -------------------------------------------------------");

        var request = new CreateUserRequest
        {
            Email = "valid@example.com", // Valid
            Name = "A",                  // Invalid but NOT in modified set
            Age = 5                      // Invalid but NOT in modified set
        };

        // Only Email was modified → only Email is validated
        ISyncValidator validator = request;
        var modifiedProperties = new HashSet<string> { "Email" };
        var result = validator.Validate(modifiedProperties);

        Console.WriteLine($"    ModifiedProperties: [{string.Join(", ", modifiedProperties)}]");
        Console.WriteLine($"    Name=\"A\" (invalid, but NOT checked)");
        Console.WriteLine($"    Age=5 (invalid, but NOT checked)");
        Console.WriteLine($"    Result: IsSuccess={result.IsSuccess}, Issues={result.Count}");
        Console.WriteLine();

        Console.WriteLine("    Note: default interface method delegates to Validate().");
        Console.WriteLine("    For entities with SG-generated change-aware overrides,");
        Console.WriteLine("    only the modified properties are validated selectively.");
        Console.WriteLine();
    }
}
