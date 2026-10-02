namespace Pragmatic.Temporal.EntityFrameworkCore.Conventions;

/// <summary>
///     Which properties of which types are temporal, decided at compile time.
/// </summary>
/// <remarks>
///     <para>
///         EF's property discovery does not recognise any of the eight temporal types as primitives, so
///         the conventions have to name those members themselves. They did it by walking
///         <c>ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)</c> and testing each
///         property's type — a closed set of eight types, checked one entity at a time at startup.
///     </para>
///     <para>
///         The generator knows every property of every type it compiles and can test the same eight
///         names against the semantic model, so the answer arrives as a list instead of a scan. Both
///         conventions read it; neither reflects.
///     </para>
///     <para>
///         A type from an assembly the generator did not run in has no entry, and its temporal
///         properties stay unmapped — the same outcome as before for a type EF had already discovered,
///         and a visible one: the column is missing rather than silently mistyped.
///     </para>
/// </remarks>
public static class TemporalPropertyRegistry
{
    private static readonly Dictionary<Type, IReadOnlyList<TemporalPropertyEntry>> Entries = [];
    private static readonly Lock Gate = new();

    /// <summary>
    ///     Registers the temporal properties of a type. Called by generated module initializers.
    /// </summary>
    /// <param name="declaringType">The type that declares them.</param>
    /// <param name="properties">Its temporal properties, in declaration order.</param>
    public static void Register(Type declaringType, params TemporalPropertyEntry[] properties)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(properties);

        lock (Gate)
            Entries[declaringType] = properties;
    }

    /// <summary>
    ///     The temporal properties of a type, or an empty list when it has none or was never registered.
    /// </summary>
    /// <param name="declaringType">The type to look up.</param>
    /// <returns>Its temporal properties.</returns>
    public static IReadOnlyList<TemporalPropertyEntry> For(Type declaringType)
    {
        lock (Gate)
            return Entries.TryGetValue(declaringType, out var properties) ? properties : [];
    }
}

/// <summary>
///     One temporal property: the name EF needs, and the CLR type it maps.
/// </summary>
/// <param name="Name">The property name.</param>
/// <param name="ClrType">The property's CLR type, nullable wrapper included where it applies.</param>
public readonly record struct TemporalPropertyEntry(string Name, Type ClrType);
