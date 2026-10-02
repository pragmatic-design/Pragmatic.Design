using Microsoft.Extensions.Options;
using Pragmatic.Composition.Attributes;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Default password policy that validates minimum length from <see cref="LocalIdentityOptions" />.
///     Replace with a custom <see cref="IPasswordPolicy" /> for complexity, history, or breach checks.
/// </summary>
[Service(Lifetime = Lifetime.Singleton)]
public sealed class DefaultPasswordPolicy(IOptions<LocalIdentityOptions> options) : IPasswordPolicy
{
    public VoidResult<IError> Validate(string password)
    {
        var minLength = options.Value.MinPasswordLength;

        if (string.IsNullOrEmpty(password) || password.Length < minLength)
            return new PasswordPolicyError($"Password must be at least {minLength} characters long.");

        return VoidResult<IError>.Success();
    }
}
