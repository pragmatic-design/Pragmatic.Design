using Pragmatic.Validation.Types;

namespace Pragmatic.Validation;

/// <summary>
///     Combined synchronous and asynchronous validator.
/// </summary>
/// <remarks>
///     <para>
///         This is the main validator interface that combines both sync and async validation.
///         Use this interface when you need to inject a complete validator in services.
///     </para>
///     <para>
///         The validator executes validation in the optimal order:
///         <list type="number">
///             <item>Sync validation first (fast, no I/O) via <see cref="ISyncValidator" /></item>
///             <item>If sync fails and fail-fast is enabled, return immediately</item>
///             <item>Otherwise, run async validation via <see cref="IAsyncValidator{T}" /></item>
///             <item>Combine errors from both phases</item>
///         </list>
///     </para>
///     <para>
///         This pattern ensures that expensive async operations (database calls, etc.)
///         are only performed when basic sync validation passes.
///     </para>
///     <para>
///         For sync-only validation, implement <see cref="ISyncValidator" />.
///         For async-only validation, implement <see cref="IAsyncValidator{T}" />.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // In a service, inject the validator:
/// public class UserService
/// {
///     private readonly IValidator&lt;CreateUserRequest&gt; _validator;
/// 
///     public async Task&lt;Result&lt;User, ValidationError&gt;&gt; CreateAsync(
///         CreateUserRequest request,
///         CancellationToken ct)
///     {
///         var validation = await _validator.ValidateAsync(request, ct);
///         if (validation.IsFailure)
///             return validation;
/// 
///         // ... create user
///     }
/// }
/// </code>
/// </example>
/// <typeparam name="T">The type to validate.</typeparam>
[global::Pragmatic.Composition.Attributes.ProvidedByHost]
public interface IValidator<in T>
{
    /// <summary>
    ///     Validates the instance using both sync and async validation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Executes sync validation first (from attributes), then async validation
    ///         (from <see cref="IAsyncValidator{T}" />). The behavior when sync validation fails
    ///         depends on <see cref="ValidationOptions.FailFast" />:
    ///     </para>
    ///     <list type="bullet">
    ///         <item><c>FailFast = true</c>: Returns immediately on first failure</item>
    ///         <item><c>FailFast = false</c>: Accumulates all errors from both phases</item>
    ///     </list>
    /// </remarks>
    /// <param name="instance">The instance to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A ValidationError with IsSuccess=true if validation passed,
    ///     or IsFailure=true with Issues containing the validation problems.
    /// </returns>
    Task<ValidationError> ValidateAsync(T instance, CancellationToken ct = default);

    /// <summary>
    ///     Validates the instance using change-aware validation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When <paramref name="modifiedProperties" /> is provided, only validation rules
    ///         relevant to the modified properties are executed. This includes:
    ///         <list type="bullet">
    ///             <item>Sync validation filtered by modified properties via <see cref="ISyncValidator.Validate(IReadOnlySet{string}?)" /></item>
    ///             <item>Async validators filtered by <see cref="IAsyncValidatorBindings{T}" /> trigger bindings</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         When <paramref name="modifiedProperties" /> is <c>null</c> (create mode),
    ///         all validation rules are executed.
    ///     </para>
    /// </remarks>
    /// <param name="instance">The instance to validate.</param>
    /// <param name="modifiedProperties">
    ///     The set of modified property names, or <c>null</c> for create mode (validates all).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A ValidationError with IsSuccess=true if validation passed,
    ///     or IsFailure=true with Issues containing the validation problems.
    /// </returns>
    /// <remarks>
    ///     Default implementation drops <paramref name="modifiedProperties" /> and runs full validation.
    ///     Implementors that support change-aware validation MUST override this method explicitly;
    ///     otherwise all properties are validated regardless of which ones changed.
    /// </remarks>
    Task<ValidationError> ValidateAsync(
        T instance,
        IReadOnlySet<string>? modifiedProperties,
        CancellationToken ct = default) => ValidateAsync(instance, ct);
}