using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     The fields an entity offers to a client-driven grid: the same list the generated bridge switches
///     on, published so the runtime adapters can consult it instead of resolving whatever name arrives.
/// </summary>
/// <remarks>
///     <para>
///         The module declares, the caller composes. <c>[GenerateGridBridge]</c> already computes this
///         list — the properties carrying <c>[Filterable]</c>, minus the ones taken back — and the
///         generator now also emits a <c>[ModuleInitializer]</c> that hands it here, so a referenced
///         module's entities are declared before anything can be asked of them.
///     </para>
///     <para>
///         ⚠️ Before this, the adapters and the bridge were two readings of one fact and the adapters
///         were the wider half: any public scalar not on a fixed denylist resolved. The client supplies
///         the field name, so a denylist covers what somebody remembered to write down. Sorting or
///         filtering on a column makes it talk without returning it — <c>sortField</c> orders by it,
///         <c>equals</c> answers whether a row holds a value — so a column the entity never published
///         was still an oracle.
///     </para>
///     <para>
///         An entity that declares nothing keeps the old guard: the denylist and the scalar check. That
///         is a smaller hole than it was — it now covers only entities with no <c>[Filterable]</c> at
///         all, which is an entity no grid was configured for — and closing it means refusing every
///         field for such an entity, which would silently empty grids that work today.
///     </para>
/// </remarks>
public static class GridFieldRegistry
{
    private static readonly ConcurrentDictionary<Type, HashSet<string>> Declared = new();

    /// <summary>Declares the fields <typeparamref name="TEntity" /> offers to a grid.</summary>
    /// <typeparam name="TEntity">The entity the fields belong to.</typeparam>
    /// <param name="fields">The property names, as the entity spells them.</param>
    public static void Declare<TEntity>(IEnumerable<string> fields)
        => Declare(typeof(TEntity), fields);

    /// <summary>The same, for generated code that holds the type rather than the type argument.</summary>
    /// <param name="entity">The entity the fields belong to.</param>
    /// <param name="fields">The property names, as the entity spells them.</param>
    public static void Declare(Type entity, IEnumerable<string> fields)
    {
        Ensure.Ensure.ThrowIfNull(entity);
        Ensure.Ensure.ThrowIfNull(fields);

        // Case-insensitive because a grid client sends the column key it was given, and casing is not
        // something the wire agreed on.
        Declared[entity] = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Whether <paramref name="field" /> is one the entity offers. <c>null</c> when the entity
    ///     declared nothing, which is different from declaring an empty list.
    /// </summary>
    /// <param name="entity">The entity being queried.</param>
    /// <param name="field">The field name the client supplied.</param>
    /// <returns><c>true</c>/<c>false</c> when a declaration exists, <c>null</c> when none does.</returns>
    public static bool? IsDeclared(Type entity, string field)
        => Declared.TryGetValue(entity, out var fields) ? fields.Contains(field) : null;

    /// <summary>Whether the entity published a list at all.</summary>
    /// <param name="entity">The entity being queried.</param>
    public static bool HasDeclaration(Type entity) => Declared.ContainsKey(entity);
}
