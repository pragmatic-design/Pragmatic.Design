namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Marks a type (class, record, struct) to generate mapping from the specified source type.
///     <para>
///         The generator will create a static <c>FromEntity()</c> method and extension methods
///         for mapping from the source type to this type.
///     </para>
/// </summary>
/// <typeparam name="TSource">The source type to map from (typically an entity).</typeparam>
/// <example>
///     <code>
///     [MapFrom&lt;User&gt;]
///     public partial record UserDto
///     {
///         public int Id { get; init; }
///         public string Email { get; init; }
///     }
/// 
///     // Generated methods:
///     // - UserDto.FromEntity(User entity)
///     // - user.ToUserDto()
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class MapFromAttribute<TSource> : Attribute
{
}