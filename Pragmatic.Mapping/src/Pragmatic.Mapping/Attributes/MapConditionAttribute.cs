namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Maps the property only when the named predicate returns <c>true</c>; otherwise the property
///     keeps its default value. The predicate is a <c>static bool</c> method on the DTO taking the
///     source entity (validated at compile time — PRAG0329).
/// </summary>
/// <example>
///     <code>
///     [MapFrom&lt;User&gt;]
///     public partial record UserDto
///     {
///         [MapCondition(nameof(ShouldMapEmail))]
///         public string? Email { get; init; }
///
///         private static bool ShouldMapEmail(User source) =&gt; source.EmailVerified;
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MapConditionAttribute(string methodName) : Attribute
{
    /// <summary>The name of the static bool predicate method on the DTO.</summary>
    public string MethodName { get; } = methodName;
}
