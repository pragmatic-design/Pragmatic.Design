namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks a record as a value object.
/// </summary>
/// <remarks>
///     <para>
///         Value objects are immutable and compared by value, not by reference.
///         They should include validation logic via a private static Validate method.
///     </para>
///     <para>
///         The source generator will generate:
///         <list type="bullet">
///             <item>
///                 <description>Create() factory method that calls Validate()</description>
///             </item>
///             <item>
///                 <description>CreateUnsafe() method for deserialization scenarios</description>
///             </item>
///         </list>
///     </para>
///     <example>
///         <code>
/// [ValueObject]
/// public partial record Email
/// {
///     public string Value { get; }
///
///     private static Result&lt;Email, ValidationError&gt; Validate(string value)
///     {
///         if (!value.Contains('@'))
///             return ValidationError.For("Email", "validation.email");
///         return new Email { Value = value };
///     }
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ValueObjectAttribute : Attribute
{
}
