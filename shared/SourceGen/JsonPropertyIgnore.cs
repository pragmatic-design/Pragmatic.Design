// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     What a property's <c>[JsonIgnore(Condition = …)]</c> asks of the writer, for a property the context still
///     carries. <c>Always</c> is not here: such a property is left out of the context, as reflection leaves it out.
/// </summary>
internal enum JsonPropertyIgnore
{
    /// <summary>No attribute: the host's <c>DefaultIgnoreCondition</c> decides.</summary>
    None,

    /// <summary><c>Never</c>: written even when the host would leave a null out.</summary>
    Never,

    /// <summary><c>WhenWritingDefault</c>: left out when it holds its type's default.</summary>
    WhenWritingDefault,

    /// <summary><c>WhenWritingNull</c>: left out when it is null.</summary>
    WhenWritingNull,
}
