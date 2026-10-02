using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Fallback property accessor using cached reflection.
/// Used when no registered accessor claims the property and the object is not a dictionary.
/// Note: This is the ONLY place in the template engine that uses reflection,
/// ⚠️ NOT opt-in: TemplateDataContext falls back to it automatically on a JIT runtime. No generator
/// emits an accessor; the ones tried first are the dictionary accessor and whatever WithAccessor
/// registered, so a typed object reaches this unless the application wrote an accessor for it.
/// </summary>
internal sealed class ReflectionPropertyAccessor : IPropertyAccessor
{
    public static ReflectionPropertyAccessor Instance { get; } = new();

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Cache = new();

    public object? GetValue(object target, string propertyName)
    {
        if (IsDeniedTarget(target)) return null;
        var prop = Cache.GetOrAdd((target.GetType(), propertyName),
            key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
        return prop?.GetValue(target);
    }

    public bool HasProperty(object target, string propertyName)
    {
        if (IsDeniedTarget(target)) return false;
        var prop = Cache.GetOrAdd((target.GetType(), propertyName),
            key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
        return prop is not null;
    }

    // Reflection over these framework types lets a template escalate from a data value to type/assembly
    // metadata, delegates, or reflection internals (e.g. someType.Assembly.Location). Even though method
    // calls and indexers are unreachable from the expression grammar, Type/Assembly-valued PROPERTIES are
    // reachable — so we refuse to reflect members off these types. Data DTOs are unaffected.
    // Type/MethodInfo/PropertyInfo/FieldInfo/etc. all derive from MemberInfo, so that one check covers them.
    private static bool IsDeniedTarget(object target) => target is
        MemberInfo or Assembly or Module or Delegate or ParameterInfo or Pointer;
}
