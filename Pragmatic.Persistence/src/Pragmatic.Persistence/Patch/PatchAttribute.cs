namespace Pragmatic.Persistence.Patch;

/// <summary>
///     Marks a DTO class to generate patch methods for applying partial updates to an entity.
/// </summary>
/// <remarks>
///     <para>
///         The source generator will generate:
///         <list type="bullet">
///             <item>
///                 <description><c>ApplyPatch(TEntity target)</c> - applies set properties to existing entity</description>
///             </item>
///             <item>
///                 <description><c>_setProperties</c> tracking for explicit property selection</description>
///             </item>
///             <item>
///                 <description>Uses generated <c>Set{Property}()</c> methods for private setters</description>
///             </item>
///         </list>
///     </para>
///     <para>
///         Properties are applied only if explicitly marked via <c>MarkSet()</c> or,
///         when no properties are marked, via nullable check (non-null = apply). The generated
///         <c>PatchJsonConverter</c>, declared on the type with <c>[JsonConverter]</c>, marks every
///         property the JSON body names — so on the wire a sent <c>null</c> clears and an absent
///         property leaves the entity alone.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to patch.</typeparam>
/// <example>
///     <code>
/// [Patch&lt;Order&gt;]
/// public partial class UpdateOrderPatch
/// {
///     public decimal? Total { get; init; }
///     public string? Description { get; init; }
/// }
///
/// // Generated:
/// public partial class UpdateOrderPatch
/// {
///     private readonly HashSet&lt;string&gt; _setProperties = [];
///     public IReadOnlySet&lt;string&gt; SetProperties =&gt; _setProperties;
///     public void MarkSet(string propertyName) =&gt; _setProperties.Add(propertyName);
///
///     public void ApplyPatch(Order target)
///     {
///         if (_setProperties.Contains(nameof(Total))) target.SetTotal(Total.Value);
///         if (_setProperties.Contains(nameof(Description))) target.SetDescription(Description);
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PatchAttribute<TEntity> : Attribute
    where TEntity : class
{
}
