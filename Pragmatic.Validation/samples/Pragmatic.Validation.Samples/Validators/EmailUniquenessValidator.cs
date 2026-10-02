using Pragmatic.Validation.Attributes;
using Pragmatic.Validation.Types;

namespace Pragmatic.Validation.Samples.Validators;

/// <summary>
///     Simulated in-memory "repository" for async validation demo.
/// </summary>
public class InMemoryUserStore
{
    private readonly HashSet<string> _existingEmails =
        ["admin@example.com", "john@example.com", "existing@test.com"];

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        return Task.FromResult(_existingEmails.Contains(email, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
///     Async validator that checks email uniqueness against a data store.
///     Demonstrates [Validator] attribute for auto DI registration and
///     IAsyncValidator&lt;T&gt; for I/O-dependent validation.
/// </summary>
[Validator]
public class EmailUniquenessValidator(InMemoryUserStore store) : IAsyncValidator<CreateUserRequest>
{
    public async Task<ValidationError> ValidateAsync(
        CreateUserRequest request,
        CancellationToken ct = default)
    {
        if (await store.EmailExistsAsync(request.Email, ct))
            return ValidationError.For("Email", "Email is already taken");

        return ValidationError.Valid;
    }
}
