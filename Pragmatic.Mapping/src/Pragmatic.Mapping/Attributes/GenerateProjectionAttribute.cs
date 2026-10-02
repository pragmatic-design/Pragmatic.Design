namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Generates an <c>Expression&lt;Func&lt;TSource, TDto&gt;&gt;</c> projection property.
///     <para>
///         Use with <see cref="MapFromAttribute{TSource}" /> to enable efficient EF Core projections
///         that translate to SQL SELECT statements.
///     </para>
///     <para>
///         <strong>Limitations:</strong> Format strings, converters, and CustomizeMapping
///         are not supported in projections (only SQL-translatable operations).
///     </para>
/// </summary>
/// <example>
///     <code>
///     [MapFrom&lt;User&gt;]
///     [GenerateProjection]
///     public partial record UserDto
///     {
///         public int Id { get; init; }
///         public string Email { get; init; }
/// 
///         [MapProperty(nameof(User.FirstName), nameof(User.LastName))]
///         public string FullName { get; init; }
///     }
/// 
///     // Usage with EF Core:
///     var users = await _db.Users
///         .Where(u => u.IsActive)
///         .Select(UserDto.Projection)
///         .ToListAsync();
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GenerateProjectionAttribute : Attribute
{
    /// <summary>
    ///     Maximum nesting depth inlined into the projection expression (default 5).
    ///     Deeper members are omitted and reported as PRAG0327 — raise this value for deeper
    ///     hierarchies, or project the nested DTO through its own <c>.Projection</c>.
    /// </summary>
    public int MaxDepth { get; set; } = 5;
}