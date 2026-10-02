using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Glossary.Models;

namespace Pragmatic.SourceGenerator.Features.Glossary.Templates;

/// <summary>
///     Emits <c>_Infra.Architecture.Generated.g.cs</c> with <c>PragmaticArchitecture.C4ContainerDiagram</c>:
///     a Mermaid container diagram of the host's modules (in-process <c>[Include]</c> vs remote
///     <c>[RemoteBoundary]</c>) as a compile-time constant. Surface it in docs or a diagnostics endpoint.
/// </summary>
internal sealed class C4DiagramTemplate : CSharpTemplate
{
    private readonly EquatableArray<C4ModuleModel> _modules;
    private readonly string _namespace;

    public C4DiagramTemplate(EquatableArray<C4ModuleModel> modules, string assemblyName)
    {
        _modules = modules;
        _namespace = string.IsNullOrEmpty(assemblyName) ? "Pragmatic.Generated" : $"{assemblyName}.Generated";
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Architecture";

    protected override bool Validate() => !_modules.IsDefaultOrEmpty;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Architecture", "Generated"), ToSourceText());

    public override void RenderFile()
    {
        AppendLine($"namespace {_namespace};");
        AppendLine();
        XmlSummary("Generated C4 container diagram (Mermaid) of the host's composed modules.");
        AppendLine("public static class PragmaticArchitecture");
        AppendLine("{");
        IncreaseIndent();
        XmlSummary("The module topology as a Mermaid flowchart.");
        AppendLine($"public const string C4ContainerDiagram = @\"{BuildMermaid()}\";");
        DecreaseIndent();
        AppendLine("}");
    }

    private string BuildMermaid()
    {
        var lines = new List<string> { "flowchart TD" };

        foreach (var module in _modules.GroupBy(m => m.Name).Select(g => g.First()).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            // In-process modules are plain boxes; remote boundaries use the subroutine shape and a note.
            lines.Add(module.IsRemote
                ? $"    {module.Name}[[{module.Name} (remote)]]"
                : $"    {module.Name}[{module.Name}]");
            lines.Add($"    Host --> {module.Name}");
        }

        return string.Join("\n", lines).Replace("\"", "\"\"");
    }
}
