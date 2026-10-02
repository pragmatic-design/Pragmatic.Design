using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Validation.Extensions;
using Pragmatic.Validation.Samples.Validators;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     DI registration surface: the explicit ServiceCollection extension methods for wiring
///     validators by hand — <c>AddSyncOnlyValidator</c>, <c>AddValidatorWithComposite</c>,
///     <c>AddAsyncValidator</c>, and <c>AddAsyncValidatorBindings</c> for change-aware filtering.
/// </summary>
public static class DiServiceRegistrationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("12. DI Registration Extensions — Wiring Validators by Hand");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowSyncOnly();
        await ShowCompositeWithAsync();
        await ShowAsyncValidatorBindings();

        Console.WriteLine();
    }

    private static async Task ShowSyncOnly()
    {
        Console.WriteLine("  12.1 AddSyncOnlyValidator<T>() — attributes only, no async validator");
        Console.WriteLine("  ---------------------------------------------------------------------");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncOnlyValidator<CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        var result = await validator.ValidateAsync(new CreateUserRequest { Email = "bad", Name = "A", Age = 5 });
        Console.WriteLine($"    IValidator<CreateUserRequest> resolved → CompositeValidator (sync only)");
        Console.WriteLine($"    IsFailure: {result.IsFailure}, Issues: {result.Count}");
        Console.WriteLine();
    }

    private static async Task ShowCompositeWithAsync()
    {
        Console.WriteLine("  12.2 AddValidatorWithComposite<TValidator, T>() — sync + async combined");
        Console.WriteLine("  ------------------------------------------------------------------------");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InMemoryUserStore>();
        // Registers IAsyncValidator<T> → EmailUniquenessValidator AND IValidator<T> → CompositeValidator<T>.
        services.AddValidatorWithComposite<EmailUniquenessValidator, CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        var result = await validator.ValidateAsync(new CreateUserRequest
        {
            Email = "admin@example.com", Name = "Admin Clone", Age = 30
        });
        Console.WriteLine($"    Sync passes, async email-uniqueness fails → IsFailure: {result.IsFailure}");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static async Task ShowAsyncValidatorBindings()
    {
        Console.WriteLine("  12.3 AddAsyncValidatorBindings<TBindings, T>() — change-aware filtering");
        Console.WriteLine("  ------------------------------------------------------------------------");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InMemoryUserStore>();
        services.AddValidatorWithComposite<EmailUniquenessValidator, CreateUserRequest>();
        // Bindings tell the CompositeValidator that the email validator only triggers
        // when "Email" is among the modified properties (or in create mode).
        services.AddAsyncValidatorBindings<EmailValidatorBindings, CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        var request = new CreateUserRequest { Email = "admin@example.com", Name = "Admin Clone", Age = 30 };

        // Modify only "Name" → email validator is SKIPPED → async failure not produced.
        var nameOnly = await validator.ValidateAsync(request, new HashSet<string> { "Name" });
        Console.WriteLine($"    Modified=[Name] → email validator skipped → IsSuccess: {nameOnly.IsSuccess}");

        // Modify "Email" → email validator RUNS → async failure produced.
        var emailChanged = await validator.ValidateAsync(request, new HashSet<string> { "Email" });
        Console.WriteLine($"    Modified=[Email] → email validator runs → IsFailure: {emailChanged.IsFailure}");

        Console.WriteLine();
        Console.WriteLine("    Note: bindings are normally SG-generated from [AsyncValidate<T>].");
        Console.WriteLine("    This hand-written binding shows the contract the SG fulfils.");
        Console.WriteLine();
    }

    /// <summary>
    ///     Hand-written <see cref="IAsyncValidatorBindings{T}" /> demonstrating the contract:
    ///     the email validator triggers in create mode (null) or when "Email" was modified.
    /// </summary>
    private sealed class EmailValidatorBindings : IAsyncValidatorBindings<CreateUserRequest>
    {
        public bool ShouldInvoke(Type validatorType, IReadOnlySet<string>? modifiedProperties)
        {
            if (validatorType != typeof(EmailUniquenessValidator))
                return false; // unknown validator → not bound

            // Create mode validates everything; otherwise only when Email changed.
            return modifiedProperties is null || modifiedProperties.Contains("Email");
        }
    }
}
