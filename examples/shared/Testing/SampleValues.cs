using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

// ReSharper disable once CheckNamespace
namespace Pragmatic.Examples.Testing;

/// <summary>
///     A value of any response type, built by reflection so a test can write every type an application answers
///     with without naming them: every member filled, or every member left at its default.
/// </summary>
/// <remarks>
///     Test code, linked into the reference applications' suites. The text it fills strings with is chosen to be
///     escaped differently by the two encoders a host could use, and to carry non-ASCII, quotes and a control
///     character.
/// </remarks>
internal static class SampleValues
{
    private const int MaxDepth = 4;

    /// <summary>A value with every member filled, two elements in every collection.</summary>
    public static object? Full(Type type) => Create(type, depth: 0, full: true);

    /// <summary>A value with every member at its default: nulls the host leaves out, empty collections.</summary>
    public static object? Empty(Type type) => Create(type, depth: 0, full: false);

    private static object? Create(Type type, int depth, bool full)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return full ? Create(underlying, depth, full) : null;

        if (Leaf(type, full) is { } leaf)
            return leaf;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 0 ? values.GetValue(values.Length - 1) : Activator.CreateInstance(type);
        }

        if (type == typeof(string))
            return full ? "Rossi & Figli <S.r.l.> \"q\" — Forlì \u0001 \U0001F600" : null;

        if (depth >= MaxDepth)
            return type.IsValueType ? Activator.CreateInstance(type) : null;

        if (Dictionary(type) is { } dictionary)
            return dictionary(depth, full);

        if (Sequence(type) is { } sequence)
            return sequence(depth, full);

        return Object(type, depth, full);
    }

    private static object? Leaf(Type type, bool full)
    {
        if (!full)
            return type.IsValueType && type != typeof(string) && !type.IsEnum && IsLeaf(type) ? Activator.CreateInstance(type) : null;

        return type switch
        {
            _ when type == typeof(bool) => true,
            _ when type == typeof(char) => '<',
            _ when type == typeof(byte) => (byte)200,
            _ when type == typeof(sbyte) => (sbyte)-5,
            _ when type == typeof(short) => (short)-300,
            _ when type == typeof(ushort) => (ushort)60000,
            _ when type == typeof(int) => -42,
            _ when type == typeof(uint) => 42u,
            _ when type == typeof(long) => long.MinValue,
            _ when type == typeof(ulong) => ulong.MaxValue,
            _ when type == typeof(float) => 0.1f,
            _ when type == typeof(double) => 1.0 / 3,
            _ when type == typeof(decimal) => 12.3400m,
            _ when type == typeof(Guid) => Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            _ when type == typeof(DateTime) => new DateTime(2026, 10, 8, 9, 30, 15, 120, DateTimeKind.Utc),
            _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 10, 8, 9, 30, 15, 120, TimeSpan.FromHours(2)),
            _ when type == typeof(DateOnly) => new DateOnly(2026, 12, 31),
            _ when type == typeof(TimeOnly) => new TimeOnly(14, 5, 9, 250),
            _ when type == typeof(TimeSpan) => TimeSpan.FromMinutes(-90.5),
            _ when type == typeof(Uri) => new Uri("https://example.com/a b?q=<x>"),
            _ when type == typeof(byte[]) => new byte[] { 1, 2, 250 },
            _ => null,
        };
    }

    private static bool IsLeaf(Type type) => Leaf(type, full: true) is not null;

    private static Func<int, bool, object?>? Dictionary(Type type)
    {
        var contract = type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                       && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(Dictionary<,>))
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (contract is null)
            return null;

        var (key, value) = (contract.GetGenericArguments()[0], contract.GetGenericArguments()[1]);
        return (depth, full) =>
        {
            var concrete = type.IsInterface ? typeof(Dictionary<,>).MakeGenericType(key, value) : type;
            var dictionary = (IDictionary)Activator.CreateInstance(concrete)!;
            if (!full)
                return dictionary;

            dictionary[Key(key, 1)] = Create(value, depth + 1, full);
            dictionary[Key(key, 2)] = Create(value, depth + 1, full: false);
            return dictionary;
        };
    }

    private static object Key(Type key, int n)
        => key == typeof(string) ? $"key <{n}> é" : Convert.ChangeType(n, key, System.Globalization.CultureInfo.InvariantCulture);

    private static Func<int, bool, object?>? Sequence(Type type)
    {
        if (type == typeof(string))
            return null;

        var element = type.IsArray
            ? type.GetElementType()
            : (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type : type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))?.GetGenericArguments()[0];
        if (element is null)
            return null;

        return (depth, full) =>
        {
            var items = new List<object?>();
            if (full)
            {
                items.Add(Create(element, depth + 1, full: true));
                items.Add(Create(element, depth + 1, full: false));
            }

            if (type.IsArray)
            {
                var array = Array.CreateInstance(element, items.Count);
                for (var i = 0; i < items.Count; i++)
                    array.SetValue(items[i], i);
                return array;
            }

            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in items)
                list.Add(item);

            return type.IsAssignableFrom(list.GetType()) ? list : Activator.CreateInstance(type, list);
        };
    }

    private static object? Object(Type type, int depth, bool full)
    {
        if (type.IsInterface || type.IsAbstract)
            return null;

        var instance = type.GetConstructor(Type.EmptyTypes) is { } parameterless
            ? parameterless.Invoke(null)
            : RuntimeHelpers.GetUninitializedObject(type);

        if (!full)
            return instance;

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;

                var value = Create(property.PropertyType, depth + 1, full);
                if (property.SetMethod is { } setter)
                    setter.Invoke(instance, [value]);
                else if (current.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
                    field.SetValue(instance, value);
            }

        return instance;
    }
}
