using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Features.I18n.Models;

namespace Pragmatic.SourceGenerator.Features.I18n.Transforms;

/// <summary>
///     Hierarchy building methods for TranslationKeysTransform.
/// </summary>
internal static partial class TranslationKeysTransform
{
    /// <summary>
    ///     Builds hierarchy with ByFile grouping.
    ///     Each file becomes a nested class.
    /// </summary>
    private static (ImmutableArray<TranslationKeyModel> RootKeys, ImmutableArray<TranslationKeyGroupModel> Groups, int
        TotalKeys)
        BuildByFileHierarchy(
            Dictionary<string, TranslationKeyModel> keys,
            ImmutableArray<string> fileNames,
            TranslationKeysConfiguration config)
    {
        var rootKeys = new List<TranslationKeyModel>();
        var groupBuilders = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);

        foreach (var kvp in keys.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var fullKey = kvp.Key;
            var keyModel = kvp.Value;
            var segments = fullKey.Split('.');

            if (segments.Length == 1)
            {
                rootKeys.Add(keyModel);
            }
            else
            {
                // Find or create group hierarchy
                for (var depth = 1; depth < segments.Length; depth++)
                {
                    var partialPath = string.Join(".", segments.Take(depth));
                    if (!groupBuilders.ContainsKey(partialPath))
                    {
                        var parentPath = depth > 1
                            ? string.Join(".", segments.Take(depth - 1))
                            : null;

                        groupBuilders[partialPath] = new GroupBuilder
                        {
                            ClassName = ToPascalCase(segments[depth - 1]),
                            ParentPath = parentPath
                        };
                    }
                }

                var groupPath = string.Join(".", segments.Take(segments.Length - 1));
                groupBuilders[groupPath].Keys.Add(keyModel);
            }
        }

        var groups = BuildGroupHierarchy(groupBuilders);
        return (rootKeys.ToImmutableArray(), groups, keys.Count);
    }

    /// <summary>
    ///     Builds flat hierarchy (no ByFile grouping).
    /// </summary>
    private static (ImmutableArray<TranslationKeyModel> RootKeys, ImmutableArray<TranslationKeyGroupModel> Groups, int
        TotalKeys)
        BuildFlatHierarchy(Dictionary<string, TranslationKeyModel> keys)
    {
        var rootKeys = new List<TranslationKeyModel>();
        var groupBuilders = new Dictionary<string, GroupBuilder>(StringComparer.Ordinal);

        foreach (var kvp in keys.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var fullKey = kvp.Key;
            var keyModel = kvp.Value;
            var segments = fullKey.Split('.');

            if (segments.Length == 1)
            {
                rootKeys.Add(keyModel);
            }
            else
            {
                for (var depth = 1; depth < segments.Length; depth++)
                {
                    var partialPath = string.Join(".", segments.Take(depth));
                    if (!groupBuilders.ContainsKey(partialPath))
                    {
                        var parentPath = depth > 1
                            ? string.Join(".", segments.Take(depth - 1))
                            : null;

                        groupBuilders[partialPath] = new GroupBuilder
                        {
                            ClassName = ToPascalCase(segments[depth - 1]),
                            ParentPath = parentPath
                        };
                    }
                }

                var groupPath = string.Join(".", segments.Take(segments.Length - 1));
                groupBuilders[groupPath].Keys.Add(keyModel);
            }
        }

        var groups = BuildGroupHierarchy(groupBuilders);
        return (rootKeys.ToImmutableArray(), groups, keys.Count);
    }

    private static ImmutableArray<TranslationKeyGroupModel> BuildGroupHierarchy(
        Dictionary<string, GroupBuilder> builders)
    {
        var rootGroups = new List<TranslationKeyGroupModel>();

        foreach (var kvp in builders.Where(b => b.Value.ParentPath == null))
        {
            var group = BuildGroup(kvp.Key, kvp.Value, builders);
            rootGroups.Add(group);
        }

        return rootGroups.ToImmutableArray();
    }

    private static TranslationKeyGroupModel BuildGroup(
        string path,
        GroupBuilder builder,
        Dictionary<string, GroupBuilder> allBuilders)
    {
        var childGroups = new List<TranslationKeyGroupModel>();
        foreach (var kvp in allBuilders.Where(b => b.Value.ParentPath == path))
            childGroups.Add(BuildGroup(kvp.Key, kvp.Value, allBuilders));

        return new TranslationKeyGroupModel(
            builder.ClassName,
            builder.Keys.ToImmutableArray(),
            childGroups.ToImmutableArray());
    }

    private class GroupBuilder
    {
        public string ClassName { get; set; } = "";
        public string? ParentPath { get; set; }
        public List<TranslationKeyModel> Keys { get; } = new();
    }
}
