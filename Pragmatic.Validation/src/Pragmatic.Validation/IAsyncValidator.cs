using Pragmatic.Validation.Types;

namespace Pragmatic.Validation;

/// <summary>
///     Asynchronous validator for complex validation logic that requires I/O.
/// </summary>
/// <remarks>
///     <para>
///         Use this interface when validation requires:
///         <list type="bullet">
///             <item>Database lookups (e.g., check if email already exists)</item>
///             <item>External service calls (e.g., validate VAT number)</item>
///             <item>File system access</item>
///             <item>Any other async operation</item>
///         </list>
///     </para>
///     <para>
///         Validators are registered automatically via the <c>[Validator]</c> attribute and
///         can be injected via DI where needed.
///     </para>
///     <para>
///         For synchronous, in-memory validation, use validation attributes which generate
///         <see cref="ISyncValidator" /> implementation.
///     </para>
///     <para>
///         For combined sync + async validation, inject <see cref="IValidator{T}" /> instead.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Validator]
/// public class CreateUserValidator : IAsyncValidator&lt;CreateUserRequest&gt;
/// {
///     private readonly IUserRepository _users;
/// 
///     public CreateUserValidator(IUserRepository users) => _users = users;
/// 
///     public async Task&lt;ValidationError&gt; ValidateAsync(
///         CreateUserRequest request,
///         CancellationToken ct = default)
///     {
///         if (await _users.ExistsAsync(request.Email, ct))
///             return ValidationError.For("Email", "validation.email.exists");
/// 
///         return ValidationError.Valid;
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="T">The type to validate.</typeparam>
public interface IAsyncValidator<in T>
{
    /// <summary>
    ///     Validates the instance asynchronously.
    /// </summary>
    /// <param name="instance">The instance to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A ValidationError with IsSuccess=true if validation passed,
    ///     or IsFailure=true with Issues containing the validation problems.
    /// </returns>
    Task<ValidationError> ValidateAsync(T instance, CancellationToken ct = default);
}