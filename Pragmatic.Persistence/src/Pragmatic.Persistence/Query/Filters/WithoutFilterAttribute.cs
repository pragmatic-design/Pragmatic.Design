namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Declares that the annotated action, mutation, or endpoint should bypass all query filters
///     for the specified entity type. The entity's filters are disabled via
///     <see cref="IQueryFilterToggle.Disable(Type)"/> before execution.
/// </summary>
/// <typeparam name="TEntity">The entity type whose filters should be disabled.</typeparam>
/// <remarks>
///     <para>
///         The <see cref="FilterMap"/> is keyed by entity type, so disabling by entity type removes the
///         entity's Pragmatic filters (soft delete, tenant, ownership, scopes) in the current scope. When
///         the entity is <c>[SoftDelete]</c>, the EF Core named filter <c>"SoftDelete"</c> is lifted too —
///         the provider cannot lift a named filter, only its name can, and without it a soft-deleted row
///         stayed out of reach.
///     </para>
///     <para>
///         ⚠️ The EF Core named <c>"Tenant"</c> filter is <b>not</b> lifted: it is the safety net that keeps
///         another tenant's rows out even where a Pragmatic filter is off. Crossing tenants is
///         <see cref="FilterModeAttribute"/>'s <c>Background</c> mode, said as such.
///     </para>
///     <para>
///         Naming a <c>VisibilityRule&lt;T&gt;</c> instead of an entity lifts that rule's named filter.
///         Use <see cref="FilterModeAttribute"/> for broader category-based control.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Query&lt;Amenity, AmenityDto&gt;]
/// [WithoutFilter&lt;Amenity&gt;]
/// public partial class AdminSearchDeletedAmenitiesQuery { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class WithoutFilterAttribute<TEntity> : Attribute where TEntity : class;
