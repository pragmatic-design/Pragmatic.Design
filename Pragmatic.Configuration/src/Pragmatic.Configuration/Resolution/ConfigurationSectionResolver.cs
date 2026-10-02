using System;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Resolves the configuration section path for an options type: the explicit
///     <c>[Configuration(SectionPath = …)]</c> if present, otherwise the type name with a common
///     <c>Options</c>/<c>Settings</c>/<c>Config</c> suffix stripped. Reflection here is acceptable — option
///     binding is inherently reflection-based — and runs once per closed generic (cached by the caller).
/// </summary>
internal static class ConfigurationSectionResolver
{
    private static readonly string[] Suffixes = ["Options", "Settings", "Configuration", "Config"];

    public static string Resolve<T>()
    {
        if (Attribute.GetCustomAttribute(typeof(T), typeof(ConfigurationAttribute)) is ConfigurationAttribute attr &&
            !string.IsNullOrEmpty(attr.SectionPath))
            return attr.SectionPath!;

        var name = typeof(T).Name;
        foreach (var suffix in Suffixes)
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name.Substring(0, name.Length - suffix.Length);

        return name;
    }
}
