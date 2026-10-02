using System;
using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.FastEnum.Models;

namespace Pragmatic.SourceGenerator.Features.FastEnum.Templates;

/// <summary>
///     Generates fast, zero-reflection extension methods for enums marked with [FastEnum].
///     Produces: ToStringFast(), IsDefined(), TryParse(), GetValues(), GetNames().
///     The JSON converter and the flag composition live in the .Json partial.
/// </summary>
internal sealed partial class FastEnumTemplate : CSharpTemplate
{
    private readonly FastEnumModel _model;

    public FastEnumTemplate(FastEnumModel model) => _model = model;

    /// <summary>
    ///     The members a switch <b>on the value</b> may branch on: one per distinct constant, first
    ///     declaration wins.
    /// </summary>
    /// <remarks>
    ///     C# lets two fields share a value — <c>Success = 0, Ok = 0</c> — and the second is an alias,
    ///     not a separate member. Emitting an arm per field then produces two identical constant
    ///     patterns and the generated file does not compile, with nothing warning first. First-declared
    ///     wins is also what <c>Enum.ToString()</c> does with an alias, so the generated name matches
    ///     the one the BCL would return.
    ///     <para>
    ///         Only for switches on the value. A switch on the <i>name</i> keeps every member: names are
    ///         unique, and an alias must still parse and still be reported as defined.
    ///     </para>
    /// </remarks>
    private List<FastEnumMemberModel> DistinctByValue()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<FastEnumMemberModel>();

        foreach (var member in _model.Members)
        {
            if (seen.Add(member.Value))
                members.Add(member);
        }

        return members;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/FastEnum";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[FastEnum] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        // Disambiguate by namespace: two [FastEnum] enums with the same simple name in
        // different namespaces (e.g. Sales.Status / Billing.Status) must not collide on
        // the AddSource hint. Keep the hint flat/dot-separated per the project convention.
        var hint = string.IsNullOrEmpty(_model.Namespace) || _model.Namespace == "<global namespace>"
            ? $"{_model.TypeName}.FastEnum.g.cs"
            : $"{_model.Namespace}.{_model.TypeName}.FastEnum.g.cs";

        return new Artifact(hint, ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Diagnostics.CodeAnalysis");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Fast, zero-reflection extension methods for <see cref=\"{_model.TypeName}\"/>.");
        AppendLine($"{_model.Accessibility} static class {_model.TypeName}Extensions");
        AppendLine("{");
        IncreaseIndent();

        RenderToStringFast();
        AppendLine();
        RenderIsDefined();

        if (_model.IsFlags)
        {
            AppendLine();
            RenderIsFlagsDefined();
            AppendLine();
            RenderFormatFlags();
        }

        AppendLine();
        RenderTryParse();
        AppendLine();
        RenderGetValues();
        AppendLine();
        RenderGetNames();

        if (_model.Members.Any(m => m.DisplayName is not null))
        {
            AppendLine();
            RenderGetDisplayName();
        }

        if (_model.HasI18n && _model.I18nKeyPrefix is not null)
        {
            AppendLine();
            RenderGetI18nKey();
            AppendLine();
            RenderGetLocalizedName();
        }

        DecreaseIndent();
        AppendLine("}");

        // Generate JsonConverter as a separate class
        AppendLine();
        RenderJsonConverter();
    }

    private void RenderToStringFast()
    {
        XmlSummary($"Converts <see cref=\"{_model.TypeName}\"/> to its string representation without reflection.");
        AppendLine($"public static string ToStringFast(this {_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return value switch");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in DistinctByValue())
            AppendLine($"{_model.TypeName}.{member.Name} => nameof({_model.TypeName}.{member.Name}),");

        // value.ToString() would work, but it is the reflection-backed path [FastEnum] exists to
        // avoid. A flag combination composes its member names; anything else prints as a number,
        // which is exactly what Enum.ToString() produces for an undeclared value.
        AppendLine(_model.IsFlags
            ? "_ => FormatFlags(value)"
            : $"_ => {NumericFormatExpression}");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderIsDefined()
    {
        // IsDefined(value)
        XmlSummary("Checks whether a value is defined in the enum without boxing.");
        AppendLine($"public static bool IsDefined({_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return value switch");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in DistinctByValue())
            AppendLine($"{_model.TypeName}.{member.Name} => true,");
        AppendLine("_ => false");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine("}");

        AppendLine();

        // IsDefined(string)
        XmlSummary("Checks whether a string name is defined in the enum.");
        AppendLine($"public static bool IsDefined(string name)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return name switch");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in _model.Members)
            AppendLine($"nameof({_model.TypeName}.{member.Name}) => true,");
        AppendLine("_ => false");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderTryParse()
    {
        if (_model.IsFlags)
        {
            RenderTryParseFlags();
            return;
        }

        XmlSummary("Tries to parse a string to the enum value without reflection.");
        AppendLine($"public static bool TryParse(string? name, out {_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        RenderExactNameSwitch();
        DecreaseIndent();
        AppendLine("}");

        AppendLine();

        // Case-insensitive overload
        XmlSummary("Tries to parse a string to the enum value (case-insensitive).");
        AppendLine($"public static bool TryParse(string? name, bool ignoreCase, out {_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (!ignoreCase) return TryParse(name, out value);");
        RenderIgnoreCaseNameSwitch();
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Flags parsing: a single name, or the comma-separated combination the writer produces.
    ///     Without this the converter cannot read back its own output.
    /// </summary>
    private void RenderTryParseFlags()
    {
        var type = _model.TypeName;

        XmlSummary("Tries to parse a string — a member name or a comma-separated flag combination — to the enum value without reflection.");
        AppendLine($"public static bool TryParse(string? name, out {type} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return TryParseCore(name, ignoreCase: false, out value);");
        DecreaseIndent();
        AppendLine("}");

        AppendLine();

        XmlSummary("Tries to parse a string — a member name or a comma-separated flag combination — to the enum value (case-insensitive).");
        AppendLine($"public static bool TryParse(string? name, bool ignoreCase, out {type} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return TryParseCore(name, ignoreCase, out value);");
        DecreaseIndent();
        AppendLine("}");

        AppendLine();

        AppendLine($"private static bool TryParseSingle(string? name, bool ignoreCase, out {type} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (!ignoreCase)");
        AppendLine("{");
        IncreaseIndent();
        RenderExactNameSwitch();
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        RenderIgnoreCaseNameSwitch();
        DecreaseIndent();
        AppendLine("}");

        AppendLine();

        AppendLine($"private static bool TryParseCore(string? name, bool ignoreCase, out {type} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (TryParseSingle(name, ignoreCase, out value))");
        AppendLine("    return true;");
        AppendLine();
        Comment("Only a combination is left to try; every token must be a declared name.");
        AppendLine("if (string.IsNullOrEmpty(name) || name!.IndexOf(',') < 0)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("value = default;");
        AppendLine("return false;");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        AppendLine($"{AccumulatorType} accumulated = 0;");
        AppendLine("foreach (var part in name.Split(','))");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (!TryParseSingle(part.Trim(), ignoreCase, out var flag))");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("value = default;");
        AppendLine("return false;");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        AppendLine($"accumulated |= ({AccumulatorType})flag;");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        AppendLine($"value = ({type})accumulated;");
        AppendLine("return true;");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderExactNameSwitch()
    {
        AppendLine("switch (name)");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in _model.Members)
        {
            AppendLine($"case nameof({_model.TypeName}.{member.Name}):");
            IncreaseIndent();
            AppendLine($"value = {_model.TypeName}.{member.Name};");
            AppendLine("return true;");
            DecreaseIndent();
        }

        AppendLine("default:");
        IncreaseIndent();
        AppendLine("value = default;");
        AppendLine("return false;");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderIgnoreCaseNameSwitch()
    {
        AppendLine("switch (name?.ToUpperInvariant())");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in _model.Members)
        {
            AppendLine($"case \"{member.Name.ToUpperInvariant()}\":");
            IncreaseIndent();
            AppendLine($"value = {_model.TypeName}.{member.Name};");
            AppendLine("return true;");
            DecreaseIndent();
        }

        AppendLine("default:");
        IncreaseIndent();
        AppendLine("value = default;");
        AppendLine("return false;");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderGetValues()
    {
        // Single backing array — `=> new[] {...}` would heap-allocate on EVERY call despite
        // the ReadOnlySpan signature promising zero-alloc (the whole pitch of FastEnum).
        var values = string.Join(", ", _model.Members.Select(m => $"{_model.TypeName}.{m.Name}"));
        AppendLine($"private static readonly {_model.TypeName}[] _values = {{ {values} }};");
        AppendLine();

        XmlSummary("Gets all defined values as a <see cref=\"ReadOnlySpan{T}\"/>.");
        AppendLine($"public static ReadOnlySpan<{_model.TypeName}> GetValues() => _values;");

        AppendLine();

        XmlSummary("Gets the number of defined values.");
        AppendLine($"public static int Count => {_model.Members.Length};");
    }

    private void RenderGetNames()
    {
        var names = string.Join(", ", _model.Members.Select(m => $"nameof({_model.TypeName}.{m.Name})"));
        AppendLine($"private static readonly string[] _names = {{ {names} }};");
        AppendLine();

        XmlSummary("Gets all defined names as a <see cref=\"ReadOnlySpan{T}\"/>.");
        AppendLine("public static ReadOnlySpan<string> GetNames() => _names;");
    }

    private void RenderGetI18nKey()
    {
        XmlSummary("Gets the i18n translation key for this enum value (convention-based).");
        AppendLine($"public static string GetI18nKey(this {_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return value switch");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in DistinctByValue())
        {
            var key = member.I18nKey ?? $"{_model.I18nKeyPrefix}.{char.ToLowerInvariant(member.Name[0])}{member.Name.Substring(1)}";
            AppendLine($"{_model.TypeName}.{member.Name} => \"{key}\",");
        }
        AppendLine($"_ => value.ToStringFast()");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderGetLocalizedName()
    {
        AddUsing("Pragmatic.Internationalization.Providers");
        AddUsing("Pragmatic.Internationalization.Context");

        XmlSummary("Gets the localized display name via the i18n provider, falling back to the enum member name.");
        AppendLine($"public static string GetLocalizedName(this {_model.TypeName} value, ILocalizationProvider? provider = null)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("var key = value.GetI18nKey();");
        AppendLine("var culture = I18NContext.Current.CultureCode;");
        AppendLine("return provider?.GetString(key, culture) ?? value.ToStringFast();");
        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderGetDisplayName()
    {
        XmlSummary("Gets the display name from [Display] or [Description] attribute, or the field name.");
        AppendLine($"public static string GetDisplayName(this {_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return value switch");
        AppendLine("{");
        IncreaseIndent();
        foreach (var member in DistinctByValue())
        {
            var display = member.DisplayName is not null
                ? $"\"{member.DisplayName}\""
                : $"nameof({_model.TypeName}.{member.Name})";
            AppendLine($"{_model.TypeName}.{member.Name} => {display},");
        }

        AppendLine($"_ => value.ToString()");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine("}");
    }
}
