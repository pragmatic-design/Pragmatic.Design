namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Configures a cascade update: when <typeparamref name="TSource" />'s property changes,
///     the SG generates an event handler to update matching entities via ExecuteUpdate.
/// </summary>
/// <typeparam name="TSource">The source entity whose property change triggers the cascade.</typeparam>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CascadeOnAttribute<TSource>(string sourceProperty) : Attribute
    where TSource : class
{
    /// <summary>
    ///     The name of the source property that triggers the cascade.
    /// </summary>
    public string SourceProperty { get; } = sourceProperty;

    /// <summary>
    ///     Optional name of a SQL-translatable boolean property on the target entity that filters which
    ///     related rows are updated (e.g. <c>"IsPending"</c> emits <c>... &amp;&amp; (e.IsPending)</c>). The
    ///     cascade runs as a set-based <c>ExecuteUpdate</c>, so this must be a property/column EF Core can
    ///     translate to SQL — NOT a method call, which would not translate. When null, all related rows
    ///     are updated.
    /// </summary>
    public string? Condition { get; init; }
}
