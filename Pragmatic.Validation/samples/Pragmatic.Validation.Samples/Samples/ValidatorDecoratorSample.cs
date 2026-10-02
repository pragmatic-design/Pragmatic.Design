using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Validation.Extensions;
using Pragmatic.Validation.Samples.Validators;
using Pragmatic.Validation.Types;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     IValidatorDecorator pattern: wrap a concrete async validator (logging / timing / caching)
///     while keeping CompositeValidator able to resolve SG-generated bindings against the
///     <em>inner</em> concrete type via <see cref="IValidatorDecorator.InnerValidatorType" />.
/// </summary>
public static class ValidatorDecoratorSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. IValidatorDecorator — Wrapping an Async Validator");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        await ShowDecoratedValidator();

        Console.WriteLine();
    }

    private static async Task ShowDecoratedValidator()
    {
        Console.WriteLine("  9.1 Logging decorator around EmailUniquenessValidator");
        Console.WriteLine("  -------------------------------------------------------");

        // The decorator wraps EmailUniquenessValidator. It still implements
        // IAsyncValidator<CreateUserRequest> so the CompositeValidator runs it,
        // but exposes InnerValidatorType so binding resolution targets the inner type.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<InMemoryUserStore>();

        // Register the concrete validator first, then a decorator that wraps it,
        // and finally the CompositeValidator as the public IValidator<T>.
        services.AddSingleton<EmailUniquenessValidator>();
        services.AddScoped<IAsyncValidator<CreateUserRequest>>(sp =>
            new LoggingEmailValidatorDecorator(sp.GetRequiredService<EmailUniquenessValidator>()));
        services.AddSyncOnlyValidator<CreateUserRequest>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateUserRequest>>();

        var request = new CreateUserRequest
        {
            Email = "admin@example.com", // exists in store → async fails
            Name = "Admin Clone",
            Age = 30
        };

        var result = await validator.ValidateAsync(request);

        Console.WriteLine($"    Wrapped validator invoked, IsFailure: {result.IsFailure}");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");

        Console.WriteLine();
        Console.WriteLine("    InnerValidatorType lets SG-generated IAsyncValidatorBindings<T>");
        Console.WriteLine("    match the wrapped concrete validator instead of the decorator.");
        Console.WriteLine($"    Decorator wraps: {typeof(EmailUniquenessValidator).Name}");
        Console.WriteLine();
    }

    /// <summary>
    ///     A decorator that adds console logging around a wrapped async validator.
    ///     Implements <see cref="IValidatorDecorator" /> so the runtime resolves bindings
    ///     against <see cref="EmailUniquenessValidator" />, not this wrapper.
    /// </summary>
    private sealed class LoggingEmailValidatorDecorator(EmailUniquenessValidator inner)
        : IAsyncValidator<CreateUserRequest>, IValidatorDecorator
    {
        public Type InnerValidatorType => typeof(EmailUniquenessValidator);

        public async Task<ValidationError> ValidateAsync(
            CreateUserRequest instance,
            CancellationToken ct = default)
        {
            Console.WriteLine($"      [decorator] before: validating {instance.Email}");
            var error = await inner.ValidateAsync(instance, ct);
            Console.WriteLine($"      [decorator] after: issues={error.Count}");
            return error;
        }
    }
}
