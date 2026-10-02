namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as auditable, adding timestamp and user tracking fields.
/// </summary>
/// <remarks>
///     <para>
///         The source generator will generate:
///         <list type="bullet">
///             <item>
///                 <description>CreatedAt (DateTimeOffset)</description>
///             </item>
///             <item>
///                 <description>CreatedBy (string?)</description>
///             </item>
///             <item>
///                 <description>UpdatedAt (DateTimeOffset?)</description>
///             </item>
///             <item>
///                 <description>UpdatedBy (string?)</description>
///             </item>
///         </list>
///     </para>
///     <para>
///         These fields are automatically populated by the SaveChanges pipeline.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditableAttribute : Attribute
{
}
