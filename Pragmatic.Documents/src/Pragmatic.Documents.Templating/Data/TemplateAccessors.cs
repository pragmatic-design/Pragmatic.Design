using System.Collections.Concurrent;

namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// The property accessors the source generator writes for the types a template is given, keyed by
/// the exact runtime type.
/// </summary>
/// <remarks>
/// Filled from a generated module initializer in each assembly that adds a typed source; not meant to
/// be called by hand, where <see cref="TemplateDataContext.WithAccessor"/> is the way to supply one.
/// <see cref="TemplateDataContext"/> consults it after the dictionary accessor and the ones passed to
/// <c>WithAccessor</c>, and before the reflective fallback.
/// </remarks>
public static class TemplateAccessors
{
    private static readonly ConcurrentDictionary<Type, IPropertyAccessor> Accessors = new();

    /// <summary>
    /// Registers the getters of <typeparamref name="T"/>'s properties, keyed by property name. Names are
    /// matched ignoring case, as the reflective fallback does. The first registration of a type wins: two
    /// assemblies that both use it write the same getters.
    /// </summary>
    public static void Register<T>(IReadOnlyDictionary<string, Func<T, object?>> getters)
    {
        ArgumentNullException.ThrowIfNull(getters);
        Accessors.TryAdd(typeof(T), new GeneratedPropertyAccessor<T>(getters));
    }

    internal static bool TryGet(Type type, out IPropertyAccessor accessor)
        => Accessors.TryGetValue(type, out accessor!);
}
