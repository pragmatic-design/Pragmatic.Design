namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>When a member is left out of the object, as System.Text.Json's ignore conditions decide it.</summary>
internal enum JsonWriterSkip
{
    /// <summary>Always written, a null included.</summary>
    Never,

    /// <summary>Left out when it is null: the host's <c>DefaultIgnoreCondition</c>, or <c>[JsonIgnore(Condition = WhenWritingNull)]</c>.</summary>
    WhenNull,

    /// <summary>Left out when it is its type's default: <c>[JsonIgnore(Condition = WhenWritingDefault)]</c>.</summary>
    WhenDefault,
}
