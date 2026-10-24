using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     The data scope rules this assembly declares on Pragmatic entities the documents engine serves, registered at module start with the engine's
///     bridge (<c>CompiledScopeRules.Register</c>): each rule's predicate for the saves, and whether the reads carry it.
/// </summary>
internal sealed class DocScopeRulesTemplate(EquatableArray<string> registrations) : CSharpTemplate
{
    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => "the data scope rules this assembly declares, registered at module start";

    public override Artifact RenderOutput() => new(VirtualFolderHints.ForAssembly("Documents", "ScopeRules"), ToSourceText());

    protected override bool Validate() => registrations.Count > 0;

    public override void RenderFile()
    {
        AppendLine("file static class DocumentScopeRules");
        AppendLine("{");
        AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("    internal static void Register()");
        AppendLine("    {");
        foreach (var registration in registrations)
            AppendLine("        " + registration);
        AppendLine("    }");
        AppendLine("}");
    }
}
