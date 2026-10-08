using System.ComponentModel;

namespace Pragmatic.Serialization;

/// <summary>
///     What a generated response writer writes, as far as the host's converters are concerned: the types it writes
///     member by member, and the enums it writes by name.
/// </summary>
/// <remarks>
///     <para>
///         Emitted by the generator beside each writer, not built by hand. The serializer uses the first converter in
///         the options that claims a type, so a writer is right only where none of its types is claimed by one, and
///         every enum is claimed by the converter whose output it reproduces. Which converters a host registers is
///         its configuration (Internationalization and Temporal add theirs, an application may add its own), so this
///         is asked of the live options, once per writer while they stay what the entry point marked.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedJsonShape
{
    /// <param name="types">Every type the writer writes itself: objects, collections, and values it formats.</param>
    /// <param name="enums">Every enum the writer writes by name.</param>
    /// <param name="enumConverters">
    ///     For each of <paramref name="enums" />, the generated <c>[FastEnum]</c> converter the host may register for
    ///     it in place of <c>JsonStringEnumConverter</c>, or null.
    /// </param>
    /// <param name="needsInfrastructureExclusion">
    ///     Whether the writer leaves out the members the host strips only when it has persistence.
    /// </param>
    public GeneratedJsonShape(Type[] types, Type[] enums, Type?[] enumConverters, bool needsInfrastructureExclusion)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(enums);
        ArgumentNullException.ThrowIfNull(enumConverters);
        if (enums.Length != enumConverters.Length)
            throw new ArgumentException("One converter, or null, per enum.", nameof(enumConverters));

        Types = types;
        Enums = enums;
        EnumConverters = enumConverters;
        NeedsInfrastructureExclusion = needsInfrastructureExclusion;
    }

    /// <summary>Every type the writer writes itself.</summary>
    public IReadOnlyList<Type> Types { get; }

    /// <summary>Every enum the writer writes by name.</summary>
    public IReadOnlyList<Type> Enums { get; }

    /// <summary>The generated converter each enum may be claimed by instead of <c>JsonStringEnumConverter</c>.</summary>
    public IReadOnlyList<Type?> EnumConverters { get; }

    /// <summary>Whether the writer leaves out the members the host strips only when it has persistence.</summary>
    public bool NeedsInfrastructureExclusion { get; }
}
