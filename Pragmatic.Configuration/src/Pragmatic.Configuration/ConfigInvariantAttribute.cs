namespace Pragmatic.Configuration;

/// <summary>
///     Marks a parameterless <see cref="bool" />-returning instance method on a <c>[Configuration]</c> options
///     class as a validation invariant: it must return <c>true</c> for the bound options to be considered
///     valid. The source generator emits an <c>IValidateOptions&lt;T&gt;</c> that runs every invariant at
///     startup (alongside DataAnnotations), so cross-property rules — that DataAnnotations cannot express —
///     fail fast.
/// </summary>
/// <remarks>
///     Example: <c>[ConfigInvariant("EndDate must be after StartDate")] public bool DatesOrdered() =&gt; End &gt; Start;</c>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ConfigInvariantAttribute(string message) : Attribute
{
    /// <summary>The error message reported when the invariant returns <c>false</c>.</summary>
    public string Message { get; } = message;
}
