using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed class BoundaryModuleMetadataTemplate : CSharpTemplate
{
    private readonly BoundaryModel _model;

    public BoundaryModuleMetadataTemplate(BoundaryModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "ModuleMetadata", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Actions.Metadata");
        AppendLine();

        var sb = new System.Text.StringBuilder();
        sb.Append("[assembly: PragmaticModuleMetadata(");
        sb.Append(Properties());
        sb.Append(")]");
        AppendLine(sb.ToString());

        // ⚠️ No runtime registration: the attribute is the whole output. A host's generator reads it
        // at compile time, which is where dependencies are checked (PRAG1601/1602).
    }

    /// <summary>The attribute's property initializers.</summary>
    private string Properties()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"BoundaryType = typeof({_model.FullTypeName})");

        if (!_model.ReadAccessTypes.IsDefaultOrEmpty)
        {
            sb.Append(", ReadAccessTypes = new[] { ");
            sb.Append(string.Join(", ", _model.ReadAccessTypes.Select(t => $"typeof({t})")));
            sb.Append(" }");
        }

        return sb.ToString();
    }
}
