using System.Collections.Concurrent;
using System.Reflection;

namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Fallback property accessor using cached reflection.
/// Used when no registered accessor claims the property and the object is not a dictionary.
/// Note: This is the ONLY place in the template engine that uses reflection,
/// ⚠️ NOT opt-in: TemplateDataContext falls back to it automatically on a JIT runtime. Tried first are
/// the dictionary accessor, whatever WithAccessor registered, and the accessors the generator writes
/// into TemplateAccessors for the types a source is added with when they are known at compile time.
/// What still reaches this: data added as <c>object</c>, anonymous types, a runtime type that is a
/// subclass of the one the source was added with, and types the generated code cannot name.
/// </summary>
internal sealed class ReflectionPropertyAccessor : IPropertyAccessor
{
    public static ReflectionPropertyAccessor Instance { get; } = new();

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Cache = new();

    /// <summary>Whether a property of <paramref name="type"/> has ever been looked up here.</summary>
    /// <remarks>
    ///     For the tests that prove a typed source is read without reflection: every lookup, found or not,
    ///     leaves its key in the cache, so an empty answer means this class never saw the type.
    /// </remarks>
    internal static bool HasRead(Type type) => Cache.Keys.Any(key => key.Item1 == type);

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
