using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Glossary.Models;

namespace Pragmatic.SourceGenerator.Features.Glossary.Templates;

/// <summary>
///     Emits <c>_Infra.UseCases.Generated.g.cs</c> with <c>PragmaticUseCases</c>: the module's use
///     cases as typed <c>UseCaseDescriptor</c>s, plus the same catalog as a Markdown constant.
/// </summary>
/// <remarks>
///     Two shapes for two readers. A test asserts against the list — an example's own suite reads the
///     catalog back — and a person reads the Markdown, the "living specification next to the code"
///     <c>[Rule]</c> describes itself as.
/// </remarks>
internal sealed class UseCaseCatalogTemplate : CSharpTemplate
{
    private const string DescriptorType = "global::Pragmatic.Authoring.UseCaseDescriptor";
    private const string ListType = "global::System.Collections.Generic.IReadOnlyList<" + DescriptorType + ">";

    private readonly EquatableArray<UseCaseModel> _entries;
    private readonly string _namespace;
    private readonly string _projectDir;

    public UseCaseCatalogTemplate(EquatableArray<UseCaseModel> entries, string assemblyName, string projectDir)
    {
        _entries = entries;
        _namespace = string.IsNullOrEmpty(assemblyName) ? "Pragmatic.Generated" : $"{assemblyName}.Generated";
        _projectDir = projectDir;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/UseCases";

    protected override bool Validate() => !_entries.IsDefaultOrEmpty;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("UseCases", "Generated"), ToSourceText());

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();
        XmlSummary("Generated use-case catalog: every [UseCase] in this module with the [Rule]s beside it.");
        AppendLine("public static class PragmaticUseCases");
        AppendLine("{");
        IncreaseIndent();

        XmlSummary("Every use case declared in this module, ordered by identifier.");
        RenderList("All", UseCases());

        AppendLine();
        XmlSummary("Business rules declared on members that carry no [UseCase] — kept, not dropped.");
        RenderList("RulesWithoutAUseCase", Orphans());

        AppendLine();
        XmlSummary("The catalog rendered as Markdown.");
        AppendLine($"public const string Markdown = @\"{BuildMarkdown()}\";");

        DecreaseIndent();
        AppendLine("}");
    }

    private List<UseCaseModel> UseCases() => _entries
        .Where(static e => e.Id is not null)
        .OrderBy(static e => e.Id, StringComparer.Ordinal)
        .ThenBy(static e => e.Target, StringComparer.Ordinal)
        .ToList();

    private List<UseCaseModel> Orphans() => _entries
        .Where(static e => e.Id is null)
        .OrderBy(static e => e.Target, StringComparer.Ordinal)
        .ToList();

    private void RenderList(string propertyName, List<UseCaseModel> entries)
    {
        AppendLine($"public static {ListType} {propertyName} {{ get; }} =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var entry in entries)
        {
            AppendLine($"new {DescriptorType}");
            AppendLine("{");
            IncreaseIndent();
            AppendLine(entry.Id is null ? "Id = null," : $"Id = \"{Escape(entry.Id)}\",");
            AppendLine(entry.Title is null ? "Title = null," : $"Title = \"{Escape(entry.Title)}\",");
            AppendLine($"Target = \"{Escape(entry.Target)}\",");
            AppendLine($"File = \"{Escape(FileOf(entry))}\",");
            AppendLine($"Line = {entry.Line},");
            AppendLine($"Rules = [{string.Join(", ", entry.Rules.Select(r => $"\"{Escape(r)}\""))}],");
            DecreaseIndent();
            AppendLine("},");
        }

        DecreaseIndent();
        AppendLine("];");
    }

    /// <summary>
    ///     The declaring file, project-relative when the build made <c>ProjectDir</c> visible and the
    ///     bare file name otherwise.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Never the absolute path the compiler reports. It names a directory on whoever built the
    ///     assembly, which would travel in every shipped package and change the generated source from
    ///     one machine to the next — a generated file that differs by machine is a snapshot nobody can
    ///     pin.
    /// </remarks>
    private string FileOf(UseCaseModel entry)
    {
        var path = entry.SourceFile.Replace('\\', '/');
        if (path.Length == 0)
            return "";

        var root = _projectDir.Replace('\\', '/');
        if (root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return path.Substring(root.Length).TrimStart('/');

        var lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? path : path.Substring(lastSlash + 1);
    }

    private string BuildMarkdown()
    {
        var lines = new List<string> { "# Use cases", "" };

        foreach (var entry in UseCases())
        {
            lines.Add(entry.Title is null ? $"## {entry.Id}" : $"## {entry.Id} — {entry.Title}");
            lines.Add($"`{entry.Target}` ({FileOf(entry)}:{entry.Line})");
            foreach (var rule in entry.Rules)
                lines.Add($"- {rule}");
            lines.Add("");
        }

        var orphans = Orphans();
        if (orphans.Count > 0)
        {
            lines.Add("## Rules with no use case");
            lines.Add("");
            foreach (var entry in orphans)
            {
                lines.Add($"### `{entry.Target}` ({FileOf(entry)}:{entry.Line})");
                foreach (var rule in entry.Rules)
                    lines.Add($"- {rule}");
                lines.Add("");
            }
        }

        // Escape double quotes for the C# verbatim string literal.
        return string.Join("\n", lines).Replace("\"", "\"\"");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
