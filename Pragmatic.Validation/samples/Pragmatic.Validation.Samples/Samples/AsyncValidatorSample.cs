using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Validation.Extensions;
using Pragmatic.Validation.Samples.Validators;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Async validator with real DI: ServiceProvider → IValidator&lt;T&gt; → CompositeValidator
///     that runs sync attributes THEN async validators in sequence.
/// </summary>
public static class AsyncValidatorSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Async Validator — DI Integration with CompositeValidator");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowFullPipeline();
        await ShowSyncFailsFirst();

        Console.WriteLine();
    }

    private static async Task ShowFullPipeline()
    {
        Console.WriteLine("  6.1 Full pipeline: sync attributes + async email uniqueness");
        Console.WriteLine("  -------------------------------------------------------------");

        // Wire up DI: store + async validator + composite
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InMemoryUserStore>();
        services.AddValidatorWithComposite<EmailUniquenessValidator, CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        // Case 1: sync valid, async fails (email exists)
        var existingEmail = new CreateUserRequest
        {
            Email = "admin@example.com", // Already in store!
            Name = "Admin Clone",
            Age = 30
        };

        var result1 = await validator.ValidateAsync(existingEmail);
        Console.WriteLine($"    Email=\"admin@example.com\" (exists in store)");
        Console.WriteLine($"    IsFailure: {result1.IsFailure}");
        foreach (var issue in result1.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");

        // Case 2: sync valid, async valid (new email)
        var newEmail = new CreateUserRequest
        {
            Email = "brand.new@example.com",
            Name = "New User",
            Age = 25
        };

        var result2 = await validator.ValidateAsync(newEmail);
        Console.WriteLine($"    Email=\"brand.new@example.com\" (not in store)");
        Console.WriteLine($"    IsSuccess: {result2.IsSuccess}");
        Console.WriteLine();
    }

    private static async Task ShowSyncFailsFirst()
    {
        Console.WriteLine("  6.2 Sync fails first — async validator is still called");
        Console.WriteLine("  --------------------------------------------------------");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InMemoryUserStore>();
        services.AddValidatorWithComposite<EmailUniquenessValidator, CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        // Both sync AND async fail
        var badRequest = new CreateUserRequest
        {
            Email = "admin@example.com", // Async: exists
            Name = "A",                  // Sync: MinLength(2)
            Age = 5                      // Sync: Range(18,120)
        };

        var result = await validator.ValidateAsync(badRequest);
        Console.WriteLine($"    Email exists + Name too short + Age out of range");
        Console.WriteLine($"    Total issues: {result.Count}");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");

        Console.WriteLine();
        Console.WriteLine("    Note: CompositeValidator runs sync THEN async.");
        Console.WriteLine("    With FailFast=true, async is skipped if sync fails.");
        Console.WriteLine();
    }
}
