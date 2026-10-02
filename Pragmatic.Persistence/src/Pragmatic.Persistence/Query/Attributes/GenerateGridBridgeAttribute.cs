namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Triggers source generation of a compile-time grid filter bridge for the entity.
///     The bridge converts a canonical <c>GridFilterRequest</c> (runtime, string-based field names)
///     into typed <c>IQueryable&lt;T&gt;</c> operations using a generated switch/case per field.
/// </summary>
/// <remarks>
///     <para>
///         The bridge is automatically generated for entities that have <c>[GridFilter&lt;TEntity&gt;]</c> DTOs.
///         Use this attribute on the entity directly if you want the bridge without a GridFilter DTO.
///     </para>
///     <para>
///         Generated extension method: <c>entity.ApplyCanonical(GridFilterRequest request)</c>
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Usage:
///     var adapter = new DevExpressGridAdapter();
///     var canonical = adapter.Adapt(loadOptions);
///     var query = dbContext.Set&lt;Order&gt;().ApplyCanonical(canonical);
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GenerateGridBridgeAttribute : Attribute;
