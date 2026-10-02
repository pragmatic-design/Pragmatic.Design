using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Glossary.Models;

namespace Pragmatic.SourceGenerator.Features.Glossary.Templates;

/// <summary>
///     Emits <c>_Infra.Glossary.Generated.g.cs</c> with <c>PragmaticGlossary.Markdown</c>: the
///     ubiquitous-language glossary (entities/value objects grouped by namespace, with their XML-doc
///     summaries) as a runtime-accessible Markdown constant — serve it like the generated OpenAPI.
/// </summary>
internal sealed class GlossaryTemplate : CSharpTemplate
{
    private readonly EquatableArray<GlossaryEntryModel> _entries;
    private readonly string _namespace;

    public GlossaryTemplate(EquatableArray<GlossaryEntryModel> entries, string assemblyName)
    {
        _entries = entries;
        _namespace = string.IsNullOrEmpty(assemblyName) ? "Pragmatic.Generated" : $"{assemblyName}.Generated";
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Glossary";

    protected override bool Validate() => !_entries.IsDefaultOrEmpty;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Glossary", "Generated"), ToSourceText());

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();
        XmlSummary("Generated ubiquitous-language glossary (domain terms grouped by boundary namespace).");
        AppendLine("public static class PragmaticGlossary");
        AppendLine("{");
        IncreaseIndent();
        XmlSummary("The glossary rendered as Markdown.");
        AppendLine($"public const string Markdown = @\"{BuildMarkdown()}\";");
        DecreaseIndent();
        AppendLine("}");
    }

    private string BuildMarkdown()
    {
        var lines = new List<string> { "# Glossary", "" };

        foreach (var group in _entries.GroupBy(e => e.Namespace).OrderBy(g => g.Key, System.StringComparer.Ordinal))
        {
            lines.Add($"## {group.Key}");
            foreach (var entry in group.OrderBy(e => e.Name, System.StringComparer.Ordinal))
            {
                var summary = (entry.Summary ?? "").Replace("\r", "").Replace("\n", " ").Trim();
                lines.Add(summary.Length == 0 ? $"- **{entry.Name}**" : $"- **{entry.Name}** — {summary}");
            }

            lines.Add("");
        }

        // Escape double quotes for the C# verbatim string literal.
        return string.Join("\n", lines).Replace("\"", "\"\"");
    }
}
