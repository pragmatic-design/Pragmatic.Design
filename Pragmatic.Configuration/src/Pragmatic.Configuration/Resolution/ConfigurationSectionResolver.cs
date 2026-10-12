using System;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Resolves the configuration section path for an options type: the explicit
///     <c>[Configuration(SectionPath = …)]</c> if present, otherwise the type name without its <c>Options</c>
///     suffix. Reflection here is acceptable — option binding is inherently reflection-based — and runs once per
///     closed generic (cached by the caller).
/// </summary>
/// <remarks>
///     The rule is the generator's (<c>ConfigurationTransform.InferSectionPath</c>), which binds
///     <c>IOptions&lt;T&gt;</c> to that section: stripping any other suffix here pointed
///     <see cref="TenantOptions{T}" /> and the change handlers at another section than <c>IOptions&lt;T&gt;</c> (#194).
/// </remarks>
internal static class ConfigurationSectionResolver
{
    private const string Suffix = "Options";

    public static string Resolve<T>()
    {
        if (Attribute.GetCustomAttribute(typeof(T), typeof(ConfigurationAttribute)) is ConfigurationAttribute attr &&
            !string.IsNullOrEmpty(attr.SectionPath))
            return attr.SectionPath!;

        var name = typeof(T).Name;
        return name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - Suffix.Length)
            : name;
    }
}
