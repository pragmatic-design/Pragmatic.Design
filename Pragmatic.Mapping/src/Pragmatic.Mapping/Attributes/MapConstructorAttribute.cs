namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Marks a constructor to be used for mapping in <c>[MapTo]</c> scenarios.
///     <para>
///         When multiple constructors exist, the generator uses the best-match algorithm.
///         Use this attribute to override and force a specific constructor.
///     </para>
/// </summary>
/// <example>
///     <code>
///     public class User
///     {
///         public int Id { get; init; }
///         public string Email { get; init; }
/// 
///         public User() { }  // Default
/// 
///         [MapConstructor]  // Use this one for mapping
///         public User(int id, string email)
///         {
///             Id = id;
///             Email = email;
///         }
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Constructor)]
public sealed class MapConstructorAttribute : Attribute
{
}