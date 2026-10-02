using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Pragmatic.Composition.Scanning;

/// <summary>
///     Reflection-based type filter for assembly scanning.
///     Prefer SG-generated registrations for AOT-safe DI configuration.
/// </summary>
[RequiresUnreferencedCode("Assembly scanning uses reflection. For AOT, use SG-generated registrations.")]
internal sealed class TypeFilter : ITypeFilter
{
    private IEnumerable<Type> _types;

    internal TypeFilter(IEnumerable<Type> types) => _types = types;

    public ITypeFilter AssignableTo<T>() => AssignableTo(typeof(T));

    public ITypeFilter AssignableTo(Type type)
    {
        if (type.IsGenericTypeDefinition)
            _types = _types.Where(t => IsAssignableToGenericType(t, type));
        else
            _types = _types.Where(type.IsAssignableFrom);
        return this;
    }

    public ITypeFilter WithAttribute<TAttribute>() where TAttribute : Attribute
    {
        _types = _types.Where(t => t.GetCustomAttribute<TAttribute>() is not null);
        return this;
    }

    public ITypeFilter InNamespace(string ns)
    {
        _types = _types.Where(t => t.Namespace == ns);
        return this;
    }

    public ITypeFilter InNamespaceOf<T>()
    {
        var ns = typeof(T).Namespace;
        _types = _types.Where(t => t.Namespace?.StartsWith(ns ?? "", StringComparison.Ordinal) == true);
        return this;
    }

    public ITypeFilter Where(Func<Type, bool> predicate)
    {
        _types = _types.Where(predicate);
        return this;
    }

    public ITypeFilter NotWhere(Func<Type, bool> predicate)
    {
        _types = _types.Where(t => !predicate(t));
        return this;
    }

    internal IEnumerable<Type> GetFilteredTypes() => _types;

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        // Walk the base-type chain iteratively rather than recursively. The chain is finite (the CLR
        // bounds inheritance depth), but an explicit loop removes any stack-depth concern and is just
        // as clear. GetInterfaces() already returns the full transitive interface set per type.
        for (var current = givenType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericType)
                return true;

            foreach (var it in current.GetInterfaces())
                if (it.IsGenericType && it.GetGenericTypeDefinition() == genericType)
                    return true;
        }

        return false;
    }
}
