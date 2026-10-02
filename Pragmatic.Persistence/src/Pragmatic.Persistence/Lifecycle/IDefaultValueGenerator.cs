namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Generates a default value for an entity property at creation time.
///     Used with <c>[ComputedDefault(Generator = typeof(...))]</c>.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TValue">The property value type.</typeparam>
[global::Pragmatic.Composition.Attributes.ProvidedByHost]
public interface IDefaultValueGenerator<in TEntity, TValue> where TEntity : class
{
    /// <summary>
    ///     Generates the default value for the property.
    /// </summary>
    /// <param name="entity">The entity being created.</param>
    /// <param name="context">The lifecycle context (clock, user, tenant).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated default value.</returns>
    Task<TValue> GenerateAsync(TEntity entity, LifecycleContext context, CancellationToken ct);
}
