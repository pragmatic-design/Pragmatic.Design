using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Redaction.Models;

namespace Pragmatic.SourceGenerator.Features.Redaction.Templates;

/// <summary>
///     Emits <c>_Infra.Redaction.RedactionMap.g.cs</c>: the compile-time map of what every type in
///     this assembly declared must not be logged, from <c>[NotLogged]</c> and <c>[PersonalData]</c>
///     alike.
/// </summary>
internal sealed class RedactionMapTemplate : CSharpTemplate
{
    private const string Member = "global::Pragmatic.Serialization.RedactedMember";

    private readonly ImmutableArray<(string TypeFqn, ImmutableArray<RedactedMemberModel> Members)> _entries;
    private readonly string _namespace;
    private readonly string? _jsonContext;

    /// <param name="members">The classified members, by the type that carries them.</param>
    /// <param name="assemblyName">The compilation's assembly name.</param>
    /// <param name="jsonContextEmitted">
    ///     Whether this compilation emits the generated JSON context, which then covers every type in
    ///     this map: the map hands it to the redactor as the metadata to serialize those types with.
    /// </param>
    public RedactionMapTemplate(ImmutableArray<RedactedMemberModel> members, string assemblyName, bool jsonContextEmitted)
    {
        _entries = members
            .GroupBy(m => m.ContainingTypeFqn, System.StringComparer.Ordinal)
            .Select(g => (g.Key, g
                .GroupBy(m => m.SerializedName, System.StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(m => m.SerializedName, System.StringComparer.Ordinal)
                .ToImmutableArray()))
            .OrderBy(e => e.Key, System.StringComparer.Ordinal)
            .ToImmutableArray();

        _namespace = NamespaceFor(assemblyName);

        // The serialization feature emits its context into "{root}.Generated" with "Global" for a
        // nameless assembly; the reference has to name the class it actually writes.
        _jsonContext = jsonContextEmitted
            ? $"global::{(string.IsNullOrEmpty(assemblyName) ? "Global" : assemblyName)}.Generated.PragmaticJsonContext"
            : null;
    }

    public string Namespace => _namespace;

    /// <summary>
    ///     Where the map and its registration live. Shared with the metadata entry so the host is told
    ///     the namespace this actually emits into.
    /// </summary>
    public static string NamespaceFor(string assemblyName)
        => string.IsNullOrEmpty(assemblyName) ? "Pragmatic.Generated" : $"{assemblyName}.Generated";

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Redaction";
    protected override string? TriggerInfo => $"[NotLogged] / [PersonalData] — {_entries.Length} declaring type(s)";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Redaction", "RedactionMap"),
        ToSourceText());

    /// <summary>
    ///     Always renders, empty included. An assembly that declares nothing must still produce a map,
    ///     so that "nothing declared" and "the generator never ran" are different runtime shapes —
    ///     the same rule the permission registry follows.
    /// </summary>
    protected override bool Validate() => true;

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();
        XmlSummary("Compile-time map of the members this assembly's types declared must not be logged.");
        Class("GeneratedRedactionMap", RenderBody,
            interfaces: ["global::Pragmatic.Serialization.IRedactionMap"],
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderBody()
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            var members = string.Join(", ", _entries[i].Members.Select(Render));
            AppendLine($"private static readonly {Member}[] Members{i} = [{members}];");
        }

        if (_entries.Length > 0)
            AppendLine();

        if (_jsonContext is not null)
        {
            XmlInheritDoc();
            AppendLine($"public global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? TypeInfoResolver => {_jsonContext}.Default;");
            AppendLine();
        }

        XmlInheritDoc();
        Method("TryGetRedactedMembers", () =>
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                AppendLine($"if (type == typeof({_entries[i].TypeFqn}))");
                AppendLine("{");
                IncreaseIndent();
                AppendLine($"members = Members{i};");
                AppendLine("return true;");
                DecreaseIndent();
                AppendLine("}");
                AppendLine();
            }

            AppendLine("members = [];");
            AppendLine("return false;");
        },
        "bool",
        new List<MethodParameter>
        {
            new() { Type = "global::System.Type", Name = "type" },
            new() { Type = $"out global::System.Collections.Generic.IReadOnlyList<{Member}>", Name = "members" },
        },
        AccessModifier.Public);
    }

    private static string Render(RedactedMemberModel m)
    {
        var name = StringHelper.CSharpLiteral(m.SerializedName);
        return m.Category is null
            ? $"new {Member}(\"{name}\", {m.Reason})"
            : $"new {Member}(\"{name}\", {m.Reason}, \"{StringHelper.CSharpLiteral(m.Category)}\")";
    }
}
