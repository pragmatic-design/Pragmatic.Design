namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as concurrency-aware, adding optimistic concurrency control via row versioning.
/// </summary>
/// <remarks>
///     <para>
///         The source generator will generate:
///         <list type="bullet">
///             <item>
///                 <description>RowVersion (uint) property configured as concurrency token</description>
///             </item>
///         </list>
///     </para>
///     <para>
///         The generated EntityConfiguration will call <c>.IsConcurrencyToken()</c> on the RowVersion property.
///         The generated repository will catch <c>DbUpdateConcurrencyException</c> and return
///         a <c>ConcurrencyError</c> via the Result pattern.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConcurrencyAwareAttribute : Attribute
{
}
