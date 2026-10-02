using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.I18n.Models;

/// <summary>
///     Represents a nested class containing translation keys.
///     Record (value-equatable) so the incremental pipeline can cache it.
/// </summary>
internal sealed record TranslationKeyGroupModel
{
    public TranslationKeyGroupModel(
        string className,
        EquatableArray<TranslationKeyModel> keys,
        EquatableArray<TranslationKeyGroupModel> nestedGroups)
    {
        ClassName = className;
        Keys = keys;
        NestedGroups = nestedGroups;
    }

    /// <summary>
    ///     The class name (e.g., "Errors").
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    ///     Direct keys in this group.
    /// </summary>
    public EquatableArray<TranslationKeyModel> Keys { get; }

    /// <summary>
    ///     Nested groups (sub-classes).
    /// </summary>
    public EquatableArray<TranslationKeyGroupModel> NestedGroups { get; }
}
