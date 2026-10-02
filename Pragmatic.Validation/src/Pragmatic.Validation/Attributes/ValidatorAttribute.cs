using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Marks a class as an asynchronous validator.
/// </summary>
/// <remarks>
///     <para>
///         Classes marked with this attribute must implement <see cref="IAsyncValidator{T}" />.
///         The source generator will:
///         <list type="number">
///             <item>Auto-register the validator in DI</item>
///             <item>
///                 Generate an <see cref="IValidator{T}" /> (combined sync+async) if the validated type has validation
///                 attributes
///             </item>
///         </list>
///     </para>
///     <para>
///         Validators can use constructor injection to access dependencies like repositories
///         or external services.
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
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ValidatorAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the service lifetime for DI registration.
    ///     Default is <see cref="ServiceLifetime.Scoped" />.
    /// </summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;
}