using System.Collections.Concurrent;

namespace Pragmatic.Temporal.Json.Behaviors;

/// <summary>
///     Maps DTO properties to the <see cref="TemporalJsonBehavior" /> that
///     <see cref="TemporalJsonModifier" /> applies during serialization.
/// </summary>
/// <remarks>
///     <para>
///         The registry is populated at startup — either by generated code (the source
///         generator emits one <see cref="Register{TDto}" /> call per attributed property)
///         or manually for apps that do not use the Pragmatic source generator. No
///         reflection is involved at any point: lookups are plain dictionary reads.
///     </para>
///     <para>
///         Property names are matched case-insensitively against the serialized JSON name,
///         so registering the CLR name (e.g. <c>CreatedAt</c>) also matches the camelCase
///         wire name (<c>createdAt</c>). Naming policies that change more than casing
///         (e.g. snake_case) require registering the serialized name.
///     </para>
/// </remarks>
public static class TemporalJsonBehaviorRegistry
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, TemporalJsonBehavior>> Entries = new();

    /// <summary>Registers a timezone behavior for a property of <typeparamref name="TDto" />.</summary>
    /// <typeparam name="TDto">The DTO type owning the property.</typeparam>
    /// <param name="propertyName">The CLR (or serialized) property name.</param>
    /// <param name="behavior">The conversion behavior to apply.</param>
    public static void Register<TDto>(string propertyName, TemporalJsonBehavior behavior)
    {
        Register(typeof(TDto), propertyName, behavior);
    }

    /// <summary>Registers a timezone behavior for a property of <paramref name="dtoType" />.</summary>
    /// <param name="dtoType">The DTO type owning the property.</param>
    /// <param name="propertyName">The CLR (or serialized) property name.</param>
    /// <param name="behavior">The conversion behavior to apply.</param>
    public static void Register(Type dtoType, string propertyName, TemporalJsonBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(dtoType);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var perType = Entries.GetOrAdd(dtoType,
            static _ => new ConcurrentDictionary<string, TemporalJsonBehavior>(StringComparer.OrdinalIgnoreCase));
        perType[propertyName] = behavior;
    }

    /// <summary>
    ///     Gets the behaviors registered for <paramref name="dtoType" />, or null when the
    ///     type has none (the fast path for the vast majority of serialized types).
    /// </summary>
    internal static IReadOnlyDictionary<string, TemporalJsonBehavior>? GetBehaviors(Type dtoType)
    {
        return Entries.TryGetValue(dtoType, out var perType) ? perType : null;
    }

    /// <summary>Removes every registration. Intended for test isolation only.</summary>
    public static void Clear()
    {
        Entries.Clear();
    }
}
