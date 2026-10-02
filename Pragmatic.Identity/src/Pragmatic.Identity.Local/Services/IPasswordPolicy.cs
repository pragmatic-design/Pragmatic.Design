using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     Validates password strength and policy compliance.
///     Consumers can implement custom password policies (complexity, history, breached check).
///     Default implementation validates against <see cref="LocalIdentityOptions.MinPasswordLength" />.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>
    ///     Validates whether the given password meets the policy requirements.
    /// </summary>
    /// <param name="password">The plaintext password to validate.</param>
    /// <returns>Success if the password meets policy, or an error describing the violation.</returns>
    VoidResult<IError> Validate(string password);
}
