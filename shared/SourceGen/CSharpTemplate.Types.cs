// =============================================================================
// Pragmatic.Design - CSharpTemplate: Type Generation Methods
// Class, Struct, Record, RecordStruct
// =============================================================================

using System.Text;

namespace Pragmatic.SourceGen;

internal abstract partial class CSharpTemplate
{
    // =============================================================================
    // Types: Class, Struct, Record
    // =============================================================================

    protected void Class(string className, Action body, string? baseType = null,
        List<string>? interfaces = null, AccessModifier accessModifier = AccessModifier.Public,
        ClassModifiers modifiers = default)
    {
        if (parent != null)
        {
            parent.Class(className, body, baseType, interfaces, accessModifier, modifiers);
            return;
        }

        var mod = BuildClassModifiers(accessModifier, modifiers);
        var inheritance = BuildInheritance(baseType, interfaces);

        AppendLine($"{mod}class {IdentifierHelper.EscapeIfKeyword(className)}{inheritance}");
        Block(body);
        AppendLine();
    }

    protected void Struct(string structName, Action body, List<string>? interfaces = null,
        AccessModifier accessModifier = AccessModifier.Public, ClassModifiers modifiers = default)
    {
        if (parent != null)
        {
            parent.Struct(structName, body, interfaces, accessModifier, modifiers);
            return;
        }

        var mod = BuildClassModifiers(accessModifier, modifiers);
        var inheritance = interfaces?.Count > 0 ? $" : {string.Join(", ", interfaces)}" : "";

        AppendLine($"{mod}struct {IdentifierHelper.EscapeIfKeyword(structName)}{inheritance}");
        Block(body);
        AppendLine();
    }

    protected void Record(string recordName, Action body, List<MethodParameter>? parameters = null,
        List<string>? interfaces = null, AccessModifier accessModifier = AccessModifier.Public,
        ClassModifiers modifiers = default)
    {
        if (parent != null)
        {
            parent.Record(recordName, body, parameters, interfaces, accessModifier, modifiers);
            return;
        }

        var mod = BuildClassModifiers(accessModifier, modifiers);
        var inheritance = interfaces?.Count > 0 ? $" : {string.Join(", ", interfaces)}" : "";
        var start = $"{mod}record {IdentifierHelper.EscapeIfKeyword(recordName)}";

        if (parameters?.Count > 0)
            AppendParameters($"{start}(", parameters, $"){inheritance}");
        else
            AppendLine($"{start}{inheritance}");

        Block(body);
        AppendLine();
    }

    protected void RecordStruct(string recordName, Action body, List<MethodParameter>? parameters = null,
        List<string>? interfaces = null, AccessModifier accessModifier = AccessModifier.Public,
        ClassModifiers modifiers = default)
    {
        if (parent != null)
        {
            parent.RecordStruct(recordName, body, parameters, interfaces, accessModifier, modifiers);
            return;
        }

        var mod = BuildClassModifiers(accessModifier, modifiers);
        var inheritance = interfaces?.Count > 0 ? $" : {string.Join(", ", interfaces)}" : "";
        var start = $"{mod}record struct {IdentifierHelper.EscapeIfKeyword(recordName)}";

        if (parameters?.Count > 0)
            AppendParameters($"{start}(", parameters, $"){inheritance}");
        else
            AppendLine($"{start}{inheritance}");

        Block(body);
        AppendLine();
    }

    private static string BuildClassModifiers(AccessModifier access, ClassModifiers mod)
    {
        // Order: access new static abstract sealed readonly partial
        // 'partial' must come immediately before 'class/struct/record'
        var sb = new StringBuilder();
        if (access != AccessModifier.NotApplicable)
            sb.Append($"{access.ToKeyword()} ");
        if (mod.New)
            sb.Append("new ");
        if (mod.IsStatic)
            sb.Append("static ");
        if (mod.Abstract)
            sb.Append("abstract ");
        if (mod.Sealed)
            sb.Append("sealed ");
        if (mod.IsReadOnly)
            sb.Append("readonly ");
        if (mod.Partial)
            sb.Append("partial ");
        return sb.ToString();
    }

    private static string BuildInheritance(string? baseType, List<string>? interfaces)
    {
        var all = new List<string>();
        if (!string.IsNullOrEmpty(baseType))
            all.Add(baseType!);
        if (interfaces != null)
            all.AddRange(interfaces);
        return all.Count > 0 ? $" : {string.Join(", ", all)}" : "";
    }
}