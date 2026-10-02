using Pragmatic.Validation.Types;

namespace Pragmatic.Validation;

/// <summary>
///     Interface for types with synchronous validation.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented automatically by the source generator when a type
///         has validation attributes. You typically don't implement this manually.
///     </para>
///     <para>
///         Synchronous validation is used for fast, in-memory checks like:
///         <list type="bullet">
///             <item>Required fields</item>
///             <item>String length</item>
///             <item>Format validation (email, phone)</item>
///             <item>Range validation</item>
///             <item>Cross-field comparison</item>
///         </list>
///     </para>
///     <para>
///         For validation that requires I/O (database, external service), use <see cref="IAsyncValidator{T}" /> instead.
///         For combined sync + async validation, use <see cref="IValidator{T}" />.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Source generator generates this from validation attributes:
/// public partial record CreateUserRequest : ISyncValidator
/// {
///     public ValidationError Validate()
///     {
///         var error = ValidationError.Valid;
///         if (string.IsNullOrEmpty(Email))
///             error = error.WithFor("Email", "validation.required");
///         return error;
///     }
/// }
/// 
/// // Usage:
/// var request = new CreateUserRequest { Email = "" };
/// var result = request.Validate();
/// if (result.IsFailure)
///     // Handle validation errors in result.Issues
/// </code>
/// </example>
public interface ISyncValidator
{
    /// <summary>
    ///     Validates this instance synchronously.
    /// </summary>
    /// <returns>
    ///     A ValidationError with IsSuccess=true if validation passed,
    ///     or IsFailure=true with Issues containing the validation problems.
    /// </returns>
    ValidationError Validate();

    /// <summary>
    ///     Validates only the specified modified properties.
    ///     Used for change-tracking-aware validation on entities.
    /// </summary>
    /// <param name="modifiedProperties">
    ///     The set of property names to validate. When null, validates ALL properties (create mode).
    /// </param>
    /// <returns>
    ///     A ValidationError with IsSuccess=true if validation passed,
    ///     or IsFailure=true with Issues containing the validation problems.
    /// </returns>
    ValidationError Validate(IReadOnlySet<string>? modifiedProperties) => Validate();
}