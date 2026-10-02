// =============================================================================
// Pragmatic.Patch - GeneratePatchAttribute
// Triggers source generation for patch DTOs
// =============================================================================

namespace Pragmatic.Patch.Attributes;

/// <summary>
///     Marks a partial type — normally a record — as a patch DTO for the specified entity type.
///     The source generator will generate <see cref="Optional{T}" /> properties
///     for each settable property on the entity, along with <c>ApplyTo()</c>,
///     <c>ModifiedProperties</c>, and a System.Text.Json converter.
/// </summary>
/// <typeparam name="TEntity">The entity type to generate patch properties from.</typeparam>
/// <remarks>
///     <para>Properties excluded from patch generation:</para>
///     <list type="bullet">
///         <item><description>Id / PersistenceId properties</description></item>
///         <item><description>IAuditable members (CreatedAt, CreatedBy, etc.)</description></item>
///         <item><description>ISoftDelete members (IsDeleted, DeletedAt, etc.)</description></item>
///         <item><description>Concurrency tokens (managed by provider-specific strategy)</description></item>
///         <item><description>Navigation/collection properties</description></item>
///     </list>
/// </remarks>
/// <example>
///     <code>
///     [GeneratePatch&lt;Guest&gt;]
///     public partial record UpdateGuestPatch;
///
///     // Generated: Optional&lt;string&gt; FirstName, Optional&lt;string&gt; LastName, etc.
///     // Generated: ApplyTo(Guest entity), ModifiedProperties, JsonConverter
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GeneratePatchAttribute<TEntity> : Attribute where TEntity : class;
