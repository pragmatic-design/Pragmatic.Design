namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Marks a type (class, record, struct) to generate mapping to the specified target type.
///     <para>
///         The generator will create a <c>ToEntity()</c> method
///         for mapping from this type to the target type.
///     </para>
///     <para>
///         For partial updates (applying changes to existing entities), use
///         <c>[GeneratePatch&lt;TEntity&gt;]</c> from <c>Pragmatic.Patch</c> instead.
///     </para>
/// </summary>
/// <typeparam name="TTarget">The target type to map to (typically an entity).</typeparam>
/// <example>
///     <code>
///     [MapTo&lt;User&gt;]
///     public partial record CreateUserDto
///     {
///         public string Email { get; init; }
///         public string FirstName { get; init; }
///     }
/// 
///     // Generated methods:
///     // - dto.ToEntity() -> User
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class MapToAttribute<TTarget> : Attribute
{
}